/*
 * Copyright © 2024, De Bitmanager
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *    http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using Bitmanager.Core;
using Bitmanager.IO;
using Bitmanager.Storage;
using Bitmanager.Web;
using Bitmanager.Xml;
using BMAlbum.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Security.AccessControl;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using static System.Formats.Asn1.AsnWriter;

namespace BMAlbum {

   [Flags]
   public enum CacheType {None=0, Small=1, Large=2, Both=3};

   public class PhotoCache : IDisposable {
      private const int MINCNT = 1; //was 10 for debugging
      private const string FN_MINDIMS = "mindims.txt";
      private const string FN_SMALLCACHE = "smallcache";
      private const string FN_LARGECACHE = "largecache";
      public readonly string CacheDir;
      private readonly int[] mindimCounters;
      public readonly Shrinker ShrinkerSmall;
      public readonly Shrinker ShrinkerLarge;

      private readonly FileStorage SmallStore;
      private readonly FileStorage LargeStore;

      private PhotoCache (string dir, Shrinker small, Shrinker large, FileStorage smallStore, FileStorage largeStore) {
         mindimCounters= new int[1200];
         CacheDir = dir;
         ShrinkerSmall = small;
         ShrinkerLarge = large;
         SmallStore = smallStore;
         LargeStore = largeStore;

         loadMindims ();
      }

      static PhotoCache() {
         WebGlobals.Instance.GlobalChangeRepository.RegisterChangeHandler (onVideoFramesUpdated);
      }


      /// <summary>
      /// Factory method to create a PhotoCache
      /// We try to reuse as max as possible from the old PhotoCache.
      /// Shrinkers are always rebuild, but caches reused if the fingerprint of the shrinker wasn't changed
      /// </summary>
      public static PhotoCache Create (WebGlobals g, XmlNode node, PhotoCache old) {
         var mp = node.ReadInt ("@max_parallel", 1);
         g.SiteLog.Log ("Setting maxParallel to {0}", mp);
         Bitmanager.ImageTools.ImageSharpHelper.SetMaxParallel (mp);

         string dir = node.ReadPath ("@dir", @"temp\cache");
         IOUtils.ForceDirectories (dir, false);

         var small = new Shrinker (node.SelectMandatoryNode ("shrink_small"));
         var large = new Shrinker (node.SelectMandatoryNode ("shrink_large"));
         FileStorage smallStore = null, largeStore = null, disposeSmall = null, disposeLarge = null;

         if (old == null) goto CREATE;
         if (dir != old.CacheDir) goto CREATE;

         if (small.FingerPrint == old.ShrinkerSmall.FingerPrint) smallStore = old.SmallStore;
         if (large.FingerPrint == old.ShrinkerLarge.FingerPrint) largeStore = old.LargeStore;

      CREATE:
         try {
            if (smallStore == null) smallStore = disposeSmall = createStorage (g, dir, small, FN_SMALLCACHE);
            if (largeStore == null) largeStore = disposeLarge = createStorage (g, dir, large, FN_LARGECACHE);

            if (old != null) {
               if (smallStore != old.SmallStore) g.DelayedDisposer.Add (old.SmallStore);
               if (largeStore != old.LargeStore) g.DelayedDisposer.Add (old.LargeStore);
            }

            var ret = new PhotoCache (dir, small, large, smallStore, largeStore);

            disposeSmall = null;
            disposeLarge = null;

            return ret;
         } finally {
            disposeSmall?.Dispose ();
            disposeLarge?.Dispose ();
         }
      }

      /// <summary>
      /// Opening or creating a storage if the shrinker indicates we need one. If not, return null
      /// </summary>
      private static FileStorage createStorage (WebGlobals g, string dir, Shrinker shrinker, string name) {
         if (!shrinker.UseCache) return null;

         string fn = Invariant.Format ("{0}_{1:X}.stor", Path.Combine (dir, name), shrinker.FingerPrint);

         if (FileStorage.IsPossibleAndExistingStorageFile (fn)) {
            try {
               return new FileStorage (fn, FileOpenMode.ReadWrite);
            } catch (Exception e) {
               string msg = Invariant.Format ("Cannot open cache file [{0}]. Will create a new one.", fn);
               g.SiteLog.Log(_LogType.ltWarning, msg);
               Logs.ErrorLog.Log (e, msg);
            }
         }
         return new FileStorage (fn, FileOpenMode.Create);
      }


      public void Dispose () {
         //Don't need to do this: we were simply terminated. 
         //WebGlobals.Instance.GlobalChangeRepository.UnregisterChangeHandler (onVideoFramesUpdated);
         SmallStore?.Dispose ();
         LargeStore?.Dispose ();
      }


      public Stream FetchSmallImage (string id, string fn, int h, Func<string,string,Image<Rgb24>> loader) {
         string cacheName = id + "&h=" + h;

         if (SmallStore != null) {
            FileEntry e;
            lock (SmallStore) {
               e = SmallStore.GetFileEntry (cacheName);
            }
            if (e != null) {
               var bytes = FileStorageAccessor.GetBytes (SmallStore, e);
               return new MemoryStream (bytes);
            }
         }

         var bm = loader (id, fn);
         try { 
            ShrinkerSmall.ApplyInPlace (ref bm, -1, h);
            var mem = new MemoryStream ();
            bm.SaveAsJpeg (mem, ShrinkerSmall.JpgEncoder);
            mem.Position = 0;

            if (ShrinkerSmall.UseCache) {
               lock (SmallStore) {
                  if (SmallStore.GetFileEntry (cacheName) == null)
                     SmallStore.AddStream (mem, cacheName, DateTime.Now, Bitmanager.Storage.CompressMethod.Store);
               }
               mem.Position = 0;
            }
            return mem;

         } finally {
            bm?.Dispose ();
         }
      }


      public Stream FetchLargeImage (string id, string fn, int srcW, int srcH, int targetW, int targetH, Func<string, string, Image<Rgb24>> loader) {
         int f = ShrinkerLarge.GetShrinkedFactorInt (srcW, srcH, targetW, targetH);
         if (f <= ShrinkerLarge.Granularity) return null; //Indicate: nothing for us...

         string cacheName = id + "&f=" + f;

         if (LargeStore != null) {
            FileEntry e;
            lock (LargeStore) {
               e = LargeStore.GetFileEntry (cacheName);
            }
            if (e != null) {
               var bytes = FileStorageAccessor.GetBytes (LargeStore, e);
               return new MemoryStream (bytes);
            }
         }

         var bm = loader (id, fn);
         try {
            ShrinkerLarge.ApplyInPlace (ref bm, targetW, targetH);
            var mem = new MemoryStream ();
            bm.SaveAsJpeg (mem, ShrinkerLarge.JpgEncoder);
            mem.Position = 0;

            if (ShrinkerLarge.UseCache) {
               lock (LargeStore) {
                  if (LargeStore.GetFileEntry (cacheName) == null)
                     LargeStore.AddStream (mem, cacheName, DateTime.Now, Bitmanager.Storage.CompressMethod.Store);
               }
               mem.Position = 0;
            }
            return mem;

         } finally {
            bm?.Dispose ();
         }
      }

      public void RegisterMinDim(int d) {
         if (d > 0 && d < mindimCounters.Length) {
            int c = mindimCounters[d] + 1;
            if (c > mindimCounters[d]) mindimCounters[d] = c;
         }
      }

      private FileStorage getStore(CacheType t) {
         return t== CacheType.Small ? SmallStore : LargeStore;
      }
      public bool Exists (string name, CacheType type) {
         var store = getStore(type);
         bool ret = false;
         if (store != null) {
            lock (store) {
               if (store.GetFileEntry (name) != null) ret = true;
            }
         }
         return ret;
      }
      public Stream Get (string name, CacheType type) {
         var store = getStore (type);
         Stream ret = null;
         if (store != null) {
            FileEntry e;
            lock (store) {
               e = store.GetFileEntry (name);
            }
            if (e != null) {
               var bytes = FileStorageAccessor.GetBytes(store, e);
               ret = new MemoryStream (bytes);
            }
         }
         return ret;
      }
      public void Set (string name, Stream strm, CacheType type) {
         var store = getStore (type);
         if (store != null)
            lock (store) {
               if (store.GetFileEntry (name) == null)
                  store.AddStream (strm, name, DateTime.Now, Bitmanager.Storage.CompressMethod.Store);
            }
      }

      public CacheType Clear (CacheType type) {
         CacheType ret = 0;
         if ((type & CacheType.Small) != 0 && SmallStore != null) {
            ret |= CacheType.Small;
            lock (SmallStore) SmallStore.Clear ();
         }
         if ((type & CacheType.Large) != 0 && LargeStore != null) {
            ret |= CacheType.Large;
            lock (LargeStore) LargeStore.Clear ();
         }
         return ret;
      }

      private static void onVideoFramesUpdated(string evKey, object context) {
         if (evKey != Events.EV_VIDEO_FRAMES_RELOADED || context == null) return;

         var g = WebGlobals.Instance;
         var photoCache = ((Settings)g.Settings).PhotoCache;
         if (photoCache == null) return;
         var frames = (FileStorage)context;
         
         int tot = photoCache.MarkStaleIfOlder(CacheType.Both, frames.Entries);
         g.SiteLog.Log(_LogType.ltInformational, "Updated photo cache for stale videoframes: {0} where marked.", tot);
      }

      private int MarkStaleIfOlder (CacheType type, IEnumerable<FileEntry> src) {
         int tot = 0;
         if ((type & CacheType.Small) != 0) tot+=markStaleIfOlder (SmallStore, src);
         if ((type & CacheType.Large) != 0) tot+=markStaleIfOlder (LargeStore, src);
         return tot;
      }

      private static int markStaleIfOlder (FileStorage store, IEnumerable<FileEntry> src) {
         int tot = 0;
         if (store != null) {
            foreach (var e in src) {
               var ourEntry = store.GetFileEntry (e.Name);
               if (ourEntry == null) continue;
               if (ourEntry.Modified < e.Modified) { ++tot; ourEntry.MarkStale (); }
            }
         }
         return tot;
      }

      public CacheStats GetCacheStats (CacheType type) {
         Dictionary<int, CountedNumber> dict = new Dictionary<int, CountedNumber> ();
         List<CountedNumber> allNumbers = null;
         var store = getStore (type);
         long size = 0;
         int count = 0;
         if (store != null) {
            List<string> names;
            lock (store) {
               size = store.Size;
               count = store.Count;
               names = store.Entries.Select (e => e.Name).ToList ();
            }
            var exprStr = type==CacheType.Small ? @"&h=\d+" : @"&f=-?\d+";
            var expr = new Regex (exprStr, RegexOptions.Compiled | RegexOptions.CultureInvariant);
            foreach (var name in names) {
               var m = expr.Match (name);
               if (!m.Success) continue;

               string str = name.Substring (m.Index + 3, m.Length - 3);
               int h = Invariant.ToInt32 (str);
               if (dict.TryGetValue (h, out var elt))
                  elt.Count++;
               else
                  dict.Add (h, new CountedNumber (h));
            }

            //if (type == CacheType.Small) {
               allNumbers = dict.Values.Where (h => h.Count >= MINCNT).ToList ();
            //} 
         }
         //if (type == CacheType.Large) {
         //   allNumbers = new List<CountedNumber> ();
         //   for (int i = 0; i < mindimCounters.Length; i++) {
         //      if (mindimCounters[i] == 0) continue;
         //      allNumbers.Add (new CountedNumber (i, mindimCounters[i]));
         //   }
         //}
         if (allNumbers == null) allNumbers = new List<CountedNumber> (0);
         else allNumbers.Sort (CountedNumber.SortCount);
         return new CacheStats (type, allNumbers, count, size);
      }



      private void loadMindims () {
         string fn = Path.Combine (CacheDir, FN_MINDIMS);
         if (!File.Exists (fn)) return;

         string[] lines = File.ReadAllLines (fn);
         for (int i = 0; i < lines.Length; i++) {
            string line = lines[i].Trim ();
            if (line.Length == 0) continue; string[] arr = line.Split (' ', StringSplitOptions.RemoveEmptyEntries);
            if (arr.Length < 2) continue;
            int d = Invariant.ToInt32 (arr[0]);
            int c = Invariant.ToInt32 (arr[1]);
            if (d >= 0 && d < mindimCounters.Length) mindimCounters[d] = c;
         }
      }

      private void saveMindims () {
         string fn = Path.Combine (CacheDir, FN_MINDIMS);
         using (var fs = IOUtils.CreateOutputStream (fn)) {
            var wtr = fs.CreateTextWriter ();
            for (int i = 0; i < mindimCounters.Length; i++) {
               if (mindimCounters[i] == 0) continue;
               wtr.Write (i);
               wtr.Write (' ');
               wtr.Write (mindimCounters[i]);
               wtr.Write ('\n');
            }
            wtr.Flush ();
         }
      }

   }



}
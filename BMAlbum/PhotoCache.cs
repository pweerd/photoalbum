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
using Bitmanager.Json;
using Bitmanager.Storage;
using System.Runtime;
using System.Text.RegularExpressions;

namespace BMAlbum {

   [Flags]
   public enum CacheType {None=0, Small=1, Large=2, Both=3};

   public class PhotoCache : IDisposable {
      private const int MINCNT = 1; //was 10 for debugging
      private const string FN_MINDIMS = "mindims.txt";
      private const string FN_SMALLCACHE = "smallcache.stor";
      private const string FN_LARGECACHE = "largecache.stor";
      public readonly string CacheDir;
      public readonly bool CacheLarge;
      private readonly int[] mindimCounters;

      private readonly FileStorage smallStore;
      private readonly FileStorage largeStore;

      public PhotoCache (string dir, bool cacheLarge) {
         mindimCounters= new int[1200];
         CacheLarge = cacheLarge;
         CacheDir = dir;
         smallStore = openStore (Path.Combine (dir, FN_SMALLCACHE));

         if (cacheLarge)
            largeStore = openStore (Path.Combine (dir, FN_LARGECACHE));

         loadMindims ();
      }

      public void RegisterMinDim(int d) {
         if (d > 0 && d < mindimCounters.Length) {
            int c = mindimCounters[d] + 1;
            if (c > mindimCounters[d]) mindimCounters[d] = c;
         }
      }

      private FileStorage getStore(CacheType t) {
         return t== CacheType.Small ? smallStore : largeStore;
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
            lock (store) {
               var strm = store.GetStream (name, false);
               if (strm != null) {
                  using (strm) {
                     var mem = new MemoryStream ();
                     strm.CopyTo (mem);
                     mem.Position = 0;
                     ret = mem;
                  }
               }
            }
         }
         return ret;
      }
      public void Set (string name, Stream strm, CacheType type) {
         var store = getStore (type);
         if (store != null) 
            lock (store) store.AddStream (strm, name, DateTime.Now, CompressMethod.Store);
      }

      public CacheType Clear (CacheType type) {
         CacheType ret = 0;
         if ((type & CacheType.Small) != 0 && smallStore != null) {
            ret |= CacheType.Small;
            lock (smallStore) smallStore.Clear ();
         }
         if ((type & CacheType.Large) != 0 && largeStore != null) {
            ret |= CacheType.Large;
            lock (largeStore) largeStore.Clear ();
         }
         return ret;
      }

      public void Close () {
         if (smallStore != null) smallStore.Close ();
         if (largeStore != null) largeStore.Close ();
         saveMindims ();
      }

      public void Dispose () {
         if (smallStore != null) smallStore.Dispose ();
         if (largeStore != null) largeStore.Dispose ();
      }

      private static FileStorage openStore (string fn) {
         if (FileStorage.IsPossibleAndExistingStorageFile (fn)) {
            try {
               return new FileStorage (fn, FileOpenMode.ReadWrite);
            } catch (Exception e) {
               Logs.ErrorLog.Log (e, "Cannot open cache file [{0}]. Will create a new one.", fn);
            }
         }
         return new FileStorage (fn, FileOpenMode.Create);
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
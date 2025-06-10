/*
 * Copyright © 2023, De Bitmanager
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

using Bitmanager.Imaging;
using Bitmanager.ImportPipeline.StreamProviders;
using Bitmanager.ImportPipeline;
using Bitmanager.IO;
using Bitmanager.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Drawing;
using Bitmanager.Core;
using System.Drawing.Imaging;
using Bitmanager.Xml;
using Bitmanager.Elastic;
using System.Text.RegularExpressions;
using Bitmanager.IR;
using System.Runtime;
using Bitmanager.Webservices;
using System.Diagnostics;
using Bitmanager.ImageTools;
using Bitmanager.AlbumTools;

namespace AlbumImporter {
   enum Orientation { None = 0, Rotate_0 = 1, Rotate_90 = 6, Rotate_180 = 3, Rotate_270 = 8 };

   public class ImportScript_Photos: ImportScriptBase {
      const string WHATSAPP = "WhatsApp";
      private static readonly Dictionary<string, int> albumIDs = new Dictionary<string, int> ();
      private DirectorySettingsCache settingsCache = new DirectorySettingsCache (null);
      private CaptionCollection captions;
      private OcrCollection ocrTexts;
      private TrackCollection tracks;
      private List<DbFace> faces;
      private FaceNames faceNames;
      private LocationByPoint locationByPointSrv;
      private MetadataProcessor mdProcessor;

      private Taggers taggers;
      private Lexicon lexicon;
      private Hypernyms hypernyms;
      private HypernymCollector hypernymCollector;
      private RegexTokenizer tokenizer;
      private static readonly string[] monthNames = { 
         "januari", 
         "februari",
         "maart",
         "april",
         "mei",
         "juni",
         "juli",
         "augustus",
         "september",
         "oktober",
         "november",
         "december"
      };
      private float facesThreshold;

      public object OnDatasourceStart (PipelineContext ctx, object value) {
         const _XmlRawMode mandatory = _XmlRawMode.EmptyToNull | _XmlRawMode.ExceptNullValue;
         const _XmlRawMode optional = _XmlRawMode.DefaultOnNullOrEmpty;

         Init (ctx, false);
         var ds = (DatasourceAdmin)value;
         var settingsNode = ds.ContextNode.SelectSingleNode ("importsettings");
         var defSettings = DirectorySettings.Default;
         if (settingsNode != null) defSettings = new DirectorySettings(settingsNode, defSettings);
         settingsCache = new DirectorySettingsCache (defSettings);
         var cache = new JsonWebserviceCacheES (new ESConnection("http://bm2:9200"), "srv_locbypos");
         locationByPointSrv = new LocationByPoint (ErrorHandling.Throw, cache, false, null);


         tokenizer = new RegexTokenizer ();
         lexicon = new Lexicon (ctx.ImportEngine.Xml.CombinePath ("dut.lex"));
         hypernyms = new Hypernyms (ctx.ImportEngine.Xml.CombinePath ("hypernyms.txt"), true);
         hypernymCollector = new HypernymCollector (tokenizer, hypernyms, lexicon);

         var dsNode = ds.ContextNode;
         captions = new CaptionCollection (ctx.ImportLog, dsNode.ReadStrRaw ("captions/@url", mandatory));
         ocrTexts = new OcrCollection (ctx.ImportLog, dsNode.ReadStrRaw ("ocr/@url", mandatory));
         tracks = new TrackCollection (ctx.ImportLog, dsNode.ReadStrRaw ("tracks/@url", optional, null));
         taggers = new Taggers (dsNode.SelectSingleNode ("taggers"));
         if (taggers.Count == 0) taggers = null;

         facesThreshold = (float)dsNode.ReadFloat ("faces/@threshold", .25);
         faces = new FaceCollection (ctx.ImportLog, dsNode.ReadStrRaw ("faces/@url", mandatory), false).GetFaces ();
         faces.Sort ((a, b) => string.Compare (a.Id, b.Id, StringComparison.Ordinal));

         faceNames = ReadFaceNames();
         ExifTool.Logger = Logs.CreateLogger ("exiftool", "exiftool");
         mdProcessor = new MetadataProcessor ();

         num_en = 0;
         num_portrait = 0;
         handleExceptions = true;
         return value;
      }

      private int num_en, num_portrait;

      public object OnDatasourceEnd (PipelineContext ctx, object value) {
         handleExceptions = true;
         mdProcessor.Dispose ();
         var ds = (DatasourceAdmin)value;
         ctx.ImportLog.Log ("Number of en captions: {0}, portrait={1}", num_en, num_portrait);

         var dirKeys = settingsCache.Keys.ToList ();
         dirKeys.Sort ();

         ctx.ImportLog.Log ("Dumping unique AlbumFetchers");
         dumpUniqueSettings (dirKeys, ctx.ImportLog, (s) => s.AlbumFetcher);
         ctx.ImportLog.Log ("Dumping unique DateFetchers");
         dumpUniqueSettings (dirKeys, ctx.ImportLog, (s) => s.DateFetcher);
         ctx.ImportLog.Log ("Dumping unique CameraFetchers");
         dumpUniqueSettings (dirKeys, ctx.ImportLog, (s) => s.CameraFetcher);
         ctx.ImportLog.Log ("Dumping unique Whatsapp detectors");
         dumpUniqueSettings (dirKeys, ctx.ImportLog, (s) => s.WhatsappDetector);
         return value;
      }

      private void dumpUniqueSettings (List<string> dirKeys, Logger logger, Func<DirectorySettings, Object> selector) {
         logger.Log ("-- {0} -> {1}", "default", selector (settingsCache.Default));
         for (int i=0; i<dirKeys.Count; i++) {
            string dir = dirKeys [i];
            var settings = settingsCache.GetSettings (dir, dir.Length);

            var obj = selector(settings);
            var parentObj = selector(settings.Parent);
            
            if (obj != parentObj) logger.Log("-- {0} -> {1}", dir, obj);
         }
      }


      private int findFirstFace (string id) {
         string key = id + "~";
         int i = -1;
         int j = faces.Count;
         while (i+1<j) {
            int m = (i+j)/ 2;
            int rc = string.Compare (faces[m].Id, key, StringComparison.Ordinal);
            if (rc < 0) i = m; else j = m;
         }
         return j < faces.Count && faces[j].Id.StartsWith (key) ? j : faces.Count;
      }


      private static string createLocation(float lat, float lon) {
         return Invariant.Format ("{0:F4},{1:F4}", lat, lon);
      }

      private static string processCaptionNL (string c) {
         if (c != null) {
            c = c.Replace ("fluitje van een cent", "gebakje").Replace("Fluitje van een cent", "Gebakje");
         }
         return c;
      }

      Logger exifLogger = Logs.CreateLogger ("exif", "import");
      public object OnPhoto (PipelineContext ctx, object value) {
         handleExceptions = false;
         idInfo = (IdInfo)value;
         var rec = ctx.Action.Endpoint.Record;

         int ix = idInfo.Id.IndexOf ('\\');
         //if (idInfo.Id.Contains ("toelen"))
         //   Debugger.Break();

         string relName = idInfo.Id.Substring(ix+1);
         rec["_id"] = idInfo.Id;
         rec["file"] = idInfo.Id;
         string captionEN = null;
         string captionNL = null;
         if (captions.TryGetValue (idInfo.Id, out var caption)) {
            if (caption.Caption_EN != null) rec["text_en"] = captionEN = caption.Caption_EN;
            if (caption.Caption_NL != null) rec["text_nl"] = captionNL = processCaptionNL(caption.Caption_NL);
         }
         if (ocrTexts.TryGetValue (idInfo.Id, out var ocrText)) {
            rec["ocr"] = ocrText.Text;
         }
         rec["ext"] = Path.GetExtension (relName).Substring (1);
         rec["root"] = idInfo.Id.Substring (0, ix-1);

         var fullName = idInfo.FileName;
         var dirName = Path.GetDirectoryName (fullName);
         var rootLen = fullName.Length - relName.Length;
         //ctx.ImportLog.Log ("rel={0}, rootlen={1}, dir={2}, full={3}", elt.RelativeName, rootLen, dirName, fullName);
         DirectorySettings dirSettings = settingsCache.GetSettings (dirName, rootLen);
         handleExceptions = true;

         dirSettings.CachedFileOrder++;
         var user = dirSettings.ForcedUser;
         if (user == null) user = idInfo.User;
         if (user != null) rec["user"] = user;

         string album=null;
         DateTime date = DateTime.MinValue;
         bool whatsapp;
         var meta = mdProcessor.GetMetadata (idInfo.FileName);
         if (meta == null) throw new BMException ("File {0} has no metadata.", idInfo.FileName);
         if (meta.Height==0 || meta.Width == 0) throw new BMException ("File {0} has no width/height.", idInfo.FileName);

         string location = meta.GpsLocation;
         rec["height"] = meta.Height;
         rec["width"] = meta.Width;
         rec["orientation"] = meta.Orientation.AsString ();
         if (!double.IsNaN (meta.DurationInSecs)) rec["duration"] = (int)meta.DurationInSecs;
         if (meta.CompressorName != null) rec["c_name"] = meta.CompressorName;
         if (meta.CompressorId != null) rec["c_id"] = meta.CompressorId;

         rec["mime"] = meta.MimeType;
         rec["type"] = meta.MimeType.StartsWith ("video") ? "video" : "photo";

         //if (idInfo.Id.StartsWith(@"D\Hidden\"))
         //   Debugger.Break ();
         date = dirSettings.DateFetcher.GetDate(idInfo, dirSettings, meta);
         album = dirSettings.AlbumFetcher.GetAlbum (idInfo, dirSettings, meta);
         var cameras = new List<string> (2);
         dirSettings.CameraFetcher.GetCamera(cameras, idInfo, meta);
         whatsapp = dirSettings.WhatsappDetector.DetectWhatsapp(idInfo, meta, ref album);

         //Sync with trackphoto's and propagate location trackid and timezone
         if (date != DateTime.MinValue && !whatsapp) {
            var pos = tracks.FindPosition (date.ToUniversalTime());
            if (pos != null) {
               if (location == null) location = createLocation (pos.Lat, pos.Lon);
               if (pos.Track.Timezone != null) rec["tz"] = pos.Track.Timezone;
               if (pos.Track.Id != null) rec["trkid"] = pos.Track.Id;
            }
         }
         if (location != null) {
            rec["location"] = location;
            var extraLocationInfo = getExtraLocationInfo (location, out var cc);
            if (extraLocationInfo != null) rec["extra_location"] = extraLocationInfo;
            if (cc != null) rec["cc"] = cc;
         }

         //Tag whatsapp images and optional replace the album name
         if (whatsapp) {
            //album = replaceAlbumForWhatsapp (relName, album);
            //date = replaceDateForWhatsapp (date);
            cameras.Add (WHATSAPP);
         }

         if (cameras.Count>0) 
            rec["camera"] = new JsonArrayValue (cameras);



         DateTime dtLocal, dtUtc;
         int y, m, d;
         if (date == DateTime.MinValue) {
            dtLocal = DateTime.MinValue;
            dtUtc = DateTime.MinValue;
            rec["sort_key"] = getAlbumId(album) * YEAR_MULTIPLIER + dirSettings.CachedFileOrder;
            rec["yyyymmdd"] = string.Empty;
            y = 0;
            m = 0;
            d = 0;
         } else {
            dtLocal = date.ToLocalTimeAssumeLocalIfUns ();
            dtUtc = date.ToUniversalTimeAssumeLocalIfUns ();
            rec["sort_key"] = toSortKey (dtUtc, dirSettings.CachedFileOrder);
            rec["date"] = dtUtc;
            rec["yyyymmdd"] = dtLocal.ToString ("yyyy-MM-dd");
            y = dtLocal.Year;
            m = dtLocal.Month;
            d = dtLocal.Day;
         }
         rec["year"] = y;
         rec["month"] = m;
         rec["day"] = d;

         var sb = new StringBuilder ();
         //if (!dirSettings.AlbumCache.TryGetValue(album, out var albumAdmin)) {
         //   albumAdmin = new AlbumCache (album, composeYearAndAlbum (sb, y, album), dtUtc);
         //   dirSettings.AlbumCache.Add(album, albumAdmin);
         //}
         rec["album"] = composeYearAndAlbum (sb, y, album);

         sb.Append (' ').Append (idInfo.Id);
         sb.Append (' ').Append (idInfo.User);

         //Include month and season
         if (m > 0) {
            sb.Append (' ').Append (monthNames[m - 1]);
            List<string> season = new List<string> ();
            switch (m) {
               case 1: season.Add ("winter"); season.Add ("~winter"); break;
               case 2: season.Add ("winter"); season.Add ("~winter"); break;
               case 3: season.Add(d < 21 ? "winter" : "lente"); season.Add ("~winter"); season.Add ("~lente"); break;
               case 4: season.Add ("lente"); season.Add ("~lente"); break;
               case 5: season.Add ("lente"); season.Add ("~lente"); break;
               case 6: season.Add (d < 21 ? "lente" : "zomer"); season.Add ("~zomer"); season.Add ("~lente"); break;
               case 7: season.Add ("zomer"); season.Add ("~zomer"); break;
               case 8: season.Add ("zomer"); season.Add ("~zomer"); break;
               case 9: season.Add (d < 21 ? "zomer" : "herfst"); season.Add ("~zomer"); season.Add ("~herfst"); break;
               case 10: season.Add ("herfst"); season.Add ("~herfst"); break;
               case 11: season.Add ("herfst"); season.Add ("~herfst"); break;
               case 12: season.Add (d < 21 ? "herfst" : "winter"); season.Add ("~herfst"); season.Add ("~winter"); break;
            }
            sb.Append (' ').Append (season[0]);
            rec["season"] = new JsonArrayValue(season);
         }

         //Extra processing for some captions
         if (captionEN != null) {
            ++num_en;
            if (isWordInString (captionEN, "wearing") ||
                isWordInString (captionEN, "wears") ||
                isWordInString (captionEN, "posing") ||
                isWordInString (captionEN, "poses") ||
                isWordInString (captionEN, "pose") ||
                isWordInString (captionEN, "posed")) {
               sb.Append (" portret");
               ++num_portrait;
            }
            if (isWordInString (captionEN, "husband")) sb.Append (" echtgenoot");
            else if (isWordInString (captionEN, "husbands")) sb.Append (" echtgenoten");
            if (isWordInString (captionEN, "wife")) sb.Append (" echtgenote");
            else if (isWordInString (captionEN, "wives")) sb.Append (" echtgenotes");
         }

         //Indicate ocr-status
         if (ocrText != null)
            sb.Append (ocrText.Valid ? " _OCR_ _OCRV_" : " _OCR_");

         var tokens = tokenizer.Tokenize (null, rec.ReadStr ("ocr", null));
         tokens = tokenizer.Tokenize (tokens, captionNL);
         for (int i=0; i<tokens.Count; i++) {
            if (tokens[i].Contains("school")) {
               sb.Append (" school");
               break;
            }
         }
         var hnyms = hypernymCollector.Collect (null, tokens, true);
         tokens.Clear ();
         hnyms = hypernymCollector.Collect (hnyms, tokenizer.Tokenize (tokens, sb.ToString()), false);
         hypernymCollector.ToString (sb, hnyms);

         rec["text"] = sb.ToString ();
         rec["album_len"] = album?.Length;

         var hideStatus = dirSettings.HideStatus;
         if (dirSettings.HideStatus == _HideStatus.None && endsWith_ (fullName))
            hideStatus = _HideStatus.External;
         switch (hideStatus) {
            case _HideStatus.External:
               rec["hide"] = hiddenForExternal;
               break;
            case _HideStatus.Always:
               rec["hide"] = hiddenAlways;
               break;
         }

         addFaces (rec, idInfo.Id);
         if (taggers != null) taggers.AddTags (user, rec);

         return null;
      }

      /// <summary>
      /// The name is composed like [year] album
      /// Special cased by removing the year from the album if it matches the spulied year
      /// </summary>
      private static string composeYearAndAlbum (StringBuilder sb, int y, string album) {
         sb.Clear ().Append ('[').Append (y).Append (']');
         if (album == null) goto EXIT_RTN;

         int start = 0;
         int end = album.Length;
         if (album.Length > 4 && y>=1000) {
            for (int i=0; i<4; i++) {
               if (album[i] != sb[i + 1]) goto TRIM_RIGHT;
            }
            for (start = 4; start < album.Length; start++) {
               if (!Trim.IsSep (album[start])) {
                  if (album[start] >= '0' && album[start] <= '9') continue;
                  break;
               }
            }

            TRIM_RIGHT:
            for (int i = 1; i <= 4; i++) {
               if (album[end-i] != sb[5-i]) goto COPY;
            }

            for (end-=4; start < end; end--) {
               if (!Trim.IsSep (album[end-1])) break;
            }
         }

      COPY:
         int len = album.Length - start;
         if (len>0) {
            sb.Append (' ');
            sb.Append (album,start,len);  
         }
      EXIT_RTN:
         return sb.ToString ();
      }

      private static bool endsWith_(string fn) {
         var idx = fn.LastIndexOf ('.');
         if (idx < 0) idx = fn.Length;
         else if (fn.IndexOf (Path.PathSeparator, idx) >= 0) idx = fn.Length;
         return fn[idx - 1] == '_';
      }

      static readonly JsonValue hiddenForExternal = new JsonStringValue ("external");
      static readonly JsonValue hiddenAlways = new JsonArrayValue ("external", "always");


      private JsonArrayValue getExtraLocationInfo (string location, out string cc) {
         var result = locationByPointSrv.GetLocationsFromPoint (location, false, 0, 1);
         if (result == null) { cc = null; return null; }
         cc = result.ReadStr ("cc", null);
         var src = result.ReadArr ("locations", null);
         return src == null || src.Count == 0 ? null : src;
      }


      private void addFaces(JsonObjectValue rec, string id) {
         int i = findFirstFace (id);
         if (i >= faces.Count) goto NO_NAMES;

         var face = faces[i];
         if (face.FaceCount == 0) goto NO_NAMES;
         int end = i + face.FaceCount;
         var arr = new JsonArrayValue ();
         rec["names"] = arr;
         rec["face_count"] = face.FaceCount;

         for (; i<end; i++) {
            face = faces[i];
            if (face.Names.Count == 0) continue;
            var name = face.Names[0];
            if (name.Id < 0 || name.Score < facesThreshold) continue;
            name.UpdateName (this.faceNames);
            arr.Add (name.ToJson ());
         }
         return;

      NO_NAMES:
         rec["face_count"] = 0;
      }


      private bool isWordInString (string outer, string arg) {
         int idx = 0;
         while (true) {
            int i = outer.IndexOf (arg, idx); 
            if (i < 0) break;
            idx = i + arg.Length;
            if (i> 0 && char.IsLetter (outer[i-1])) continue;
            if (idx >= outer.Length || !char.IsLetter (outer[idx])) return true;
         }
         return false;
      }

      private int getAlbumId(string album) {
         int albumId=0;
         if (!string.IsNullOrEmpty (album)) {
            if (!albumIDs.TryGetValue (album, out albumId)) {
               albumId = 1 + albumIDs.Count;
               albumIDs[album] = albumId;
            }
         }
         return albumId;
      }


      const long SS_MULTIPLIER = 1000L;
      const long MM_MULTIPLIER = 100 * SS_MULTIPLIER;
      const long HH_MULTIPLIER = 100 * MM_MULTIPLIER;
      const long DAY_MULTIPLIER = 100 * HH_MULTIPLIER;
      const long MONTH_MULTIPLIER = 100 * DAY_MULTIPLIER;
      const long YEAR_MULTIPLIER = 100 * MONTH_MULTIPLIER;

      private static long toSortKey (DateTime date, int order) {
         return
            order
            + date.Year * YEAR_MULTIPLIER
            + date.Month * MONTH_MULTIPLIER
            + date.Day * DAY_MULTIPLIER
            + date.Hour * HH_MULTIPLIER
            + date.Minute * MM_MULTIPLIER
            + date.Second * SS_MULTIPLIER;
      }
   }
}

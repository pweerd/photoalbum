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

using AlbumImporter.Captions;
using AlbumImporter.FaceRecognition;
using AlbumImporter.Ocr;
using AlbumImporter.PHash;
using AlbumImporter.Tracks;
using Bitmanager.AlbumTools;
using Bitmanager.Core;
using Bitmanager.Elastic;
using Bitmanager.ImageTools;
using Bitmanager.ImportPipeline;
using Bitmanager.IR;
using Bitmanager.Json;
using Bitmanager.Webservices;
using Bitmanager.Xml;
using System.Diagnostics;
using System.Text;
using System.Xml;

namespace AlbumImporter {

   public class ImportScript_Photos: ImportScriptBase {
      const string WHATSAPP = "WhatsApp";
      private static readonly Dictionary<string, int> albumIDs = new Dictionary<string, int> ();
      private DirectorySettingsCache settingsCache = new DirectorySettingsCache (null);
      private PHashCollection phashes;
      private CaptionCollection captions;
      private OcrCollection ocrTexts;
      private TrackCollection tracks;
      private List<DbFace> faces;
      private FaceNames faceNames;
      private LocationByPoint locationByPointSrv;
      private MetadataProcessor mdProcessor;
      private CaptionReplacers enCaptionReplacer, trCaptionReplacer;
      private IJsonWebserviceCache geoCache;

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

      public object OnDatasourceStart (PipelineContext ctx, object value) {
         const _XmlRawMode mandatory = _XmlRawMode.EmptyToNull | _XmlRawMode.ExceptNullValue;
         const _XmlRawMode optional = _XmlRawMode.DefaultOnNullOrEmpty;

         Init (ctx, false);
         var ds = (DatasourceAdmin)value;
         var settingsNode = ds.ContextNode.SelectSingleNode ("importsettings");
         var defSettings = DirectorySettings.Default;
         if (settingsNode != null) defSettings = new DirectorySettings(settingsNode, defSettings);
         settingsCache = new DirectorySettingsCache (defSettings);

         var dsNode = ds.ContextNode;
         var geoCacheNode = dsNode.SelectSingleNode("geo_name_by_point/cache");
         var geoCacheFile = geoCacheNode.ReadPath("@file", null);
         var geoCacheUrl = geoCacheNode.ReadStr("@url", null);
         if (geoCacheFile != null && geoCacheUrl != null)
            throw new BMNodeException (geoCacheNode, "Cannot specify both file and url.");
         geoCache = JsonWebserviceCache.Create ((geoCacheFile ?? geoCacheUrl) ?? "geo-cache-file.txt.gz");
         locationByPointSrv = new LocationByPoint (ErrorHandling.Throw, geoCache, false, null);


         tokenizer = new RegexTokenizer ();
         lexicon = new Lexicon (ctx.ImportEngine.Xml.CombinePath ("dut.lex"));
         hypernyms = new Hypernyms (ctx.ImportEngine.Xml.CombinePath ("hypernyms.txt"), true);
         hypernymCollector = new HypernymCollector (tokenizer, hypernyms, lexicon);

         phashes = new PHashCollection(ctx.ImportLog, IndexInfo.Create(dsNode.ReadStrRaw("hashes/@url", optional), false), false);
         captions = new CaptionCollection (ctx.ImportLog, IndexInfo.Create(dsNode.ReadStrRaw ("captions/@url", mandatory), false), false);
         ocrTexts = new OcrCollection (ctx.ImportLog, IndexInfo.Create(dsNode.ReadStrRaw ("ocr/@url", mandatory), false), false, false);
         var tracksNode = dsNode.SelectSingleNode ("tracks");
         string[] urls = null;
         string[] files = null;
         if (tracksNode != null) {
            urls = readStrCollection (tracksNode, "url");
            files = readStrCollection (tracksNode, "file");
         }
         tracks = new TrackCollection (ctx.ImportLog, urls, files);
         taggers = new Taggers (dsNode.SelectSingleNode ("taggers"));
         if (taggers.Count == 0) taggers = null;

         var facesIndex = IndexInfo.Create(dsNode.ReadStrRaw ("faces/@url", mandatory), false);
         base.CheckLegacyFaceNamesIndex (facesIndex);
         faces = new FaceCollection (ctx.ImportLog, facesIndex, false).GetFaces ();
         faces.Sort ((a, b) => string.Compare (a.Id, b.Id, StringComparison.Ordinal));

         faceNames = ReadFaceNames();
         ExifTool.Logger = Logs.CreateLogger ("exiftool", "exiftool");
         mdProcessor = new MetadataProcessor ();

         enCaptionReplacer = new CaptionReplacers (dsNode.SelectMandatoryNode("captions/replacers[@lang='en']"));
         trCaptionReplacer = new CaptionReplacers(dsNode.SelectMandatoryNode("captions/replacers[@lang='translated']"));

         num_en = 0;
         num_portrait = 0;
         handleExceptions = true;
         return value;
      }

      /// <summary>
      /// Reads a string[] from a node.
      /// - either 1 element from the attribute
      /// - or a collection from the named nodes
      /// </summary>
      private static string[] readStrCollection (XmlNode node, string name) {
         var str = node.ReadStr ("@" + name, null);
         if (str != null) return new string[] { str };
         var coll = node.SelectNodes (name);
         if (coll.Count == 0) return null;
         var ret = new string[coll.Count];
         for (int i=0; i<ret.Length; i++) {
            ret[i] = coll[i].ReadStr(null);
         }
         return ret;
      }

      private int num_en, num_portrait;

      public object OnDatasourceEnd (PipelineContext ctx, object value) {
         handleExceptions = true;
         mdProcessor.Dispose ();

         //Save geo cache
         try {
            geoCache?.Close ();
         } finally {
            geoCache = null;
         }

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

      static readonly Logger exifLogger = Logs.CreateLogger ("exif", "import");

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
            string txt;
            txt = enCaptionReplacer.Replace(caption.Caption_EN);
            if (!string.IsNullOrEmpty(txt)) rec["text_en"] = captionEN = txt;
            txt = trCaptionReplacer.Replace(caption.Caption_NL);
            if (!string.IsNullOrEmpty(txt)) rec["text_nl"] = captionNL = txt;
         }
         if (ocrTexts.TryGetValue(idInfo.Id, out var ocrText)) {
            rec["ocr"] = ocrText.Text;
         }
         if (phashes.TryGetValue(idInfo.Id, out var phash)) {
            rec["ph1txt"] = PHash.PHash.FingerprintToString(phash.PHash1);
            rec["ph2txt"] = PHash.PHash.FingerprintToString(phash.PHash2);
            unchecked {
               rec["ph1"] = (long)phash.PHash1;
               rec["ph2"] = (long)phash.PHash2;
            }
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
         user ??= idInfo.User;
         if (user != null) rec["user"] = user;

         string album;
         DateTime date = DateTime.MinValue;
         bool whatsapp;
         var meta = mdProcessor.GetMetadata (idInfo.FileName);
         if (meta == null) throw new BMException ("File {0} has no metadata.", idInfo.FileName);
         if (meta.Height==0 || meta.Width == 0) throw new BMException ("File {0} has no width/height.", idInfo.FileName);

         string location = meta.GpsLocation;
         if (location != null) rec["gps_location"] = location;

         rec["height"] = meta.Height;
         rec["width"] = meta.Width;
         rec["orientation"] = meta.Orientation.AsString(); 
         if (!double.IsNaN (meta.DurationInSecs)) rec["duration"] = (int)meta.DurationInSecs;
         if (meta.CompressorName != null) rec["c_name"] = meta.CompressorName;
         if (meta.CompressorId != null) rec["c_id"] = meta.CompressorId;
         string tz = meta.TzOriginal ?? meta.TzCreated;

         if (tz != null) {
            rec["tz_photo"] = tz;
            rec["tz_photo_utc"] = tz.EndsWith ("00:00");
         }

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
            //if (date > new DateTime (2025, 9, 28)) Debugger.Break ();
            var pos = tracks.FindPosition (date.ToUniversalTime());
            if (pos != null) {
               if (location == null) location = createLocation (pos.Lat, pos.Lon);
               if (pos.Track.Timezone != null) tz =pos.Track.Timezone;
               if (pos.Track.Id != null) rec["trkid"] = pos.Track.Id;
            }
         }
         rec["tz"] = tz;

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
         taggers?.AddTags (user, rec);

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

      private List<DbFace> tmpFaces = new List<DbFace>(32);
      private void addFaces(JsonObjectValue rec, string id) {
         int i = findFirstFace (id);
         if (i >= faces.Count) goto NO_NAMES;

         var face = faces[i];
         if (face.FaceCount == 0) goto NO_NAMES;
         int end = i + face.FaceCount;
         var arr = new JsonArrayValue ();
         rec["names"] = arr;
         rec["face_count"] = face.FaceCount;

         //Sort the faces on descending area, and assign an index to them
         //Both valid and ignored items will be exported
         //We can filter them later on status or on the Nth largest face
         tmpFaces.Clear ();
         for (; i < end; i++) {
            face = faces[i];
            if (face.Status == DbFace.StatusFlags.Valid || face.Status == DbFace.StatusFlags.Ignored)
               tmpFaces.Add (face);
         }
         if (tmpFaces.Count == 0) return;  //We don't want to overwrite face_count

         tmpFaces.Sort((a,b)=>b.RelPos.GetArea() - a.RelPos.GetArea());
         int pos = 0;
         float areaAtPos = tmpFaces[0].RelPos.GetArea ();
         for (i=0; i<tmpFaces.Count; i++) {
            face = tmpFaces[i];
            float area = face.RelPos.GetArea();
            while (area < areaAtPos) {
               areaAtPos *= .6f;
               pos++;
            }
            var name = face.Name;
            name.UpdateName (this.faceNames);
            var json = new JsonObjectValue();
            json.Add ("id", name.Id);
            json.Add ("index", pos);
            json.Add ("status", face.Status == DbFace.StatusFlags.Valid ? "V" : "I");
            json.Add ("name", name.Name);
            json.Add ("match_score", name.Score);
            json.Add ("match_score_raw", name.ScoreRaw);
            json.Add ("confidence", name.Confidence);
            json.Add ("relarea", face.RelPos.GetRelArea ());
            json.Add ("ratio", face.FaceRatio);
            json.Add ("ratio_01", face.FaceRatio_1);
            arr.Add (json);
         }
         return;

      NO_NAMES:
         rec["face_count"] = 0;
      }


      private static bool isWordInString (string outer, string arg) {
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

      private static int getAlbumId(string album) {
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

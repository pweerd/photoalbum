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

using Bitmanager.Core;
using Bitmanager.Elastic;
using Bitmanager.Gps;
using Bitmanager.Json;

namespace AlbumImporter.Tracks {

   public class TrackPhoto {
      public readonly string Id;
      public readonly string TrackId;
      public readonly string Timezone;
      public readonly float Lat, Lon;

      public TrackPhoto (GenericDocument doc) {
         Id = doc.Id;
         var src = doc._Source;
         TrackId = src.ReadStr ("trkid");
         Timezone = src.ReadStr ("tz");
         Lat = (float)src.ReadDbl ("lat");
         Lon = (float)src.ReadDbl ("lon");
      }
   }

   public class TrackCollection {
      private readonly List<TrackAdmin> tracks;
      public TrackCollection (Logger logger, string[] urls, string[] files) {
         tracks = new List<TrackAdmin> ();
         logger.Log (_LogType.ltTimerStart, "Tracks, loading...");
         int cnt;
         if (urls != null) {
            foreach (var url in urls) {
               cnt = tracks.Count;
               logger.Log ("-- Loading from url [{0}]...", url);
               loadTracksFromUrl (logger, tracks, url);
               logger.Log ("-- Loaded {0} tracks from url [{1}].", tracks.Count-cnt, url);
            }
         }
         if (files != null) {
            foreach (var fn in files) {
               cnt = tracks.Count;
               logger.Log ("-- Loading from file/dir [{0}]...", fn);
               loadTracksFromFile (logger, tracks, fn);
               logger.Log ("-- Loaded {0} tracks from file/dir [{1}].", tracks.Count - cnt, fn);
            }
         }

         tracks.Sort (TrackAdmin.SortTracksOnDate);
         logger.Log (_LogType.ltTimerStop, "Tracks, Loaded {0} tracks in total", tracks.Count);
      }

      private void loadTracksFromUrl (Logger logger, List<TrackAdmin> dst, string url) {
         var req = Utils.CreateESRequest (url);
         req.Query = new ESTermQuery ("", "");
         req.Fields = "trackdata";
         req.SetSource ("meta", null);

         var bq = new ESBoolQuery ();
         bq.AddFilter (new ESTermQuery ("type", "meta"));
         bq.AddFilter (new ESTermQuery ("meta.type", "track"));
         req.Query = bq;

         using (var e = new ESRecordEnum (req)) {
            foreach (var d in e) {
               var src = d._Source;
               var meta = src.ReadObj ("meta");
               var data = (JsonArrayValue)d._Fields["trackdata"];
               src["trackdata"] = JsonObjectValue.Parse (data[0]);
               var track = new Track (_GeoNamesMode.Disabled, null);
               track.LoadFromJson (src);
               dst.Add (new TrackAdmin (d.Id, track));
            }
         }
      }

      private void loadTracksFromFile (Logger logger, List<TrackAdmin> dst, string fn) {
         Track track;
         FileAttributes attr = File.GetAttributes (fn);

         if (attr.HasFlag (FileAttributes.Directory)) {
            foreach (var f in Directory.EnumerateFiles(fn, "*.gpx")) {
               track = new Track (_GeoNamesMode.Disabled, null);
               track.LoadGPX (f, string.Empty, _CleanupMode.None);
               dst.Add (new TrackAdmin (f, track, 60, 60));
            }
            return;
         }
         track = new Track (_GeoNamesMode.Disabled, null);
         track.LoadGPX (fn, string.Empty, _CleanupMode.None);
         dst.Add (new TrackAdmin (fn, track, 60, 60));
      }


      public Position FindPosition (DateTime dt) {
         return TrackAdmin.FindPosition (tracks, dt);
      }

   }
   public class TrackPhotoCollection {
      private Dictionary<string, TrackPhoto> dict;
      public TrackPhotoCollection (Logger logger, string url) {
         dict = new Dictionary<string, TrackPhoto> ();

         if (logger != null) logger.Log ("Loading track photos from {0}", url);

         if (url != null) {
            var req = Utils.CreateESRequest (url);
            using (var e = new ESRecordEnum (req)) {
               e.AcceptIndexNotExist = true;
               foreach (var rec in e) {
                  var photo = new TrackPhoto (rec);
                  dict.Add (photo.Id, photo);
               }
            }
         }
         if (logger != null) logger.Log ("Loaded {0} track photos from {1}", dict.Count, url);
      }

      public bool TryGetValue (string id, out TrackPhoto trackPhoto) {
         id = Path.GetFileNameWithoutExtension (id).ToLowerInvariant ();
         return dict.TryGetValue (id, out trackPhoto);
      }

   }
}

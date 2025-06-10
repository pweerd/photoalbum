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
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AlbumImporter {

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
      public TrackCollection (Logger logger, string url) {
         tracks = new List<TrackAdmin> ();
         if (url == null) {
            logger.Log (_LogType.ltTimer, "Tracks, nothing loaded: url not specified");
            return;
         }
         var req = Utils.CreateESRequest (url);
         req.Query = new ESTermQuery ("", "");

         logger.Log (_LogType.ltTimerStart, "Tracks, loading...");
         addTracksLoadedFromES (tracks, req);

         ////Maybe from files too...
         logger.Log (_LogType.ltTimer, "Tracks, Loaded {0} tracks from ES", tracks.Count);
         tracks.Sort (TrackAdmin.SortTracksOnDate);
         logger.Log (_LogType.ltTimerStop, "Tracks, Sorted {0} tracks", tracks.Count);
      }

      public Position FindPosition (DateTime dt) {
         return TrackAdmin.FindPosition (tracks, dt);
      }

      private void addTracksLoadedFromES (List<TrackAdmin> dst, ESSearchRequest req) {
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

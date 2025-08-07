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
using Bitmanager.Json;

namespace BMAlbum.Core {
   /// <summary>
   /// Represents a boundingBox for GEO coordinates
   /// </summary>
   public class GeoBoundingBox {
      public readonly float NWLat, NWLon, SELat, SELon;

      public GeoBoundingBox (string bbox) {
         var arr = bbox.SplitStandard ();
         NWLat = Invariant.ToFloat (arr[0]);
         NWLon = Invariant.ToFloat (arr[1]);
         SELat = Invariant.ToFloat (arr[2]);
         SELon = Invariant.ToFloat (arr[3]);
         if (NWLat < SELat) { var t = NWLat; NWLat = SELat; SELat = t; }
      }
      public GeoBoundingBox (float nwLat, float nwLon, float seLat, float seLon) {
         NWLat = nwLat;
         NWLon = nwLon;
         SELat = seLat;
         SELon = seLon;
         if (NWLat < SELat) { var t = NWLat; NWLat = SELat; SELat = t; }
      }

      public GeoBoundingBox Zoom (float factor) {
         float h = (NWLat - SELat) * (factor / 2);
         float w = (SELon - NWLon) * (factor / 2);
         float centerLat = (SELat + NWLat) / 2;
         float centerLon = (SELon + NWLon) / 2;
         return new GeoBoundingBox (centerLat - h, centerLon - w, centerLat + h, centerLon + w);
      }

      /// <summary>
      /// Divides the boundingBox into parts*parts equal sub-boxes,
      /// and returns the part-index that contains the given geo-point
      /// </summary>
      public int GetPartIndex (float lat, float lon, int parts) {
         float h = (NWLat - SELat) / parts;
         float w = (SELon - NWLon) / parts;

         int y = (int)((lat - SELat) / h);
         if (y < 0) y=0;
         else if (y>=parts) y=parts-1;

         int x = (int)((lon - NWLon) / w);
         if (x < 0) x = 0;
         else if (x >= parts) x = parts - 1;
         return y * parts + x;
      }

      /// <summary>
      /// Divides the boundingBox into parts*parts equal sub-boxes,
      /// and returns the representing GeoBoundingBox for the box associated with the part-index.
      /// </summary>
      public GeoBoundingBox GetPart (int partIndex, int parts) {
         float h = (NWLat - SELat) / parts;
         float w = (SELon - NWLon) / parts;
         int x = partIndex % parts;
         int y = partIndex / parts;
         if (x >= parts || y >= parts) throw new BMException ("Invalid part-index [{0}]: parts={1}.", partIndex, parts);
         var seLat = SELat + y * h;
         var nwLat = seLat + h;
         var nwLon = NWLon + x * w;
         var seLon = nwLon + w;
         return new GeoBoundingBox (nwLat, nwLon, seLat, seLon);
      }

      /// <summary>
      /// Divides the boundingBox into parts*parts equal sub-boxes,
      /// and returns the part-index that contains the given geo-point
      /// </summary>
      public int GetPartIndex (string loc, int parts) {
         var arr = loc.SplitStandard ();
         return GetPartIndex (Invariant.ToFloat (arr[0]), Invariant.ToFloat (arr[1]), parts);
      }

      public override string ToString() {
         return Invariant.Format ("{0:R},{1:R},{2:R},{3:R}",
            NWLat, NWLon,
            SELat, SELon
         );
      }

      public JsonArrayValue ToJsonArr () {
         var ret = new JsonArrayValue ();
         ret.Add (NWLat);
         ret.Add (NWLon);
         ret.Add (SELat);
         ret.Add (SELon);
         return ret;
      }
   }
}

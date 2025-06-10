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
using Bitmanager.Gps;

namespace AlbumImporter {
   public class TrackAdmin {
      private static readonly IComparer<DateTime> dtComparer = Comparer<DateTime>.Default;

      public readonly string Id;
      public readonly string Timezone;
      public readonly DateTime StartTimeLimit, EndTimeLimit;
      private readonly List<TrackPoint> Points;

      public TrackAdmin (string id, Track t, int defBeforeSecs=7200, int defAfterSecs=30000) {
         Id = id;
         Points = new List<TrackPoint> ();

         Timezone = t.TimeZones==null ? null : t.TimeZones[0];
         int secs = t.IncludePhotosBeforeSec;
         if (secs < 0) secs = defBeforeSecs;
         StartTimeLimit = t.StartTime.AddSeconds (-secs);
         var item = t.Items[0];
         Points.Add (new TrackPoint (StartTimeLimit, (float)item.Lat, (float)item.Lon));

         secs = t.IncludePhotosAfterSec;
         if (secs < 0) secs = defAfterSecs; // 7200;
         EndTimeLimit = t.EndTime.AddSeconds (secs);

         for (int i=0; i<t.Items.Count; i++) {
            Points.Add (new TrackPoint(t.Items[i]));
         }
         item = t.Items[^1];
         Points.Add (new TrackPoint (EndTimeLimit, (float)item.Lat, (float)item.Lon));
      }

      public override string ToString () {
         return Invariant.Format ("{0}, start/end: {1}/{2}, limits: {3}/{4}",
            Id,
            Invariant.ToString (Points[1].Time),
            Invariant.ToString (Points[^2].Time),
            Invariant.ToString (StartTimeLimit),
            Invariant.ToString (EndTimeLimit));

      }

      public Position FindPosition (DateTime t) {
         int i = -1;
         int j = Points.Count;
         //Invariant:
         // Points[i].Time < time
         // Points[j].Time >= time
         //So we find the surrounding points in i and j
         while (i+1<j) {
            int m = (i + j) / 2;
            if (Points[m].Time < t) i++; else j--;
         }
         return (i >= 0 && j < Points.Count) ? new Position (this, t, Points[i], Points[j]) : null;
      }

      /// <summary>
      /// Returns the best possible approximation for the location
      /// 
      public static Position FindPosition (List<TrackAdmin> list, DateTime t) {
         int i = -1;
         int j = list.Count;
         //Invariant:
         // list[i].endTime < time
         // list[j].endTime >= time
         //So we find the first possible matching track in j
         while (i + 1 < j) {
            int m = (i + j) / 2;
            if (list[m].EndTimeLimit < t) i++; else j--;
         }
         if (j >= list.Count) return null;

         Position bestPos = null;
         for (; j<list.Count; j++) {
            if (list[j].StartTimeLimit > t) break;
            Position pos = list[j].FindPosition (t);
            if (pos == null) continue;
            if (bestPos == null || bestPos.DiffInSecs > pos.DiffInSecs) bestPos = pos;
         }
         return bestPos;
      }

      public static int SortTracksOnDate (TrackAdmin x, TrackAdmin y) {
         int rc = dtComparer.Compare (x.StartTimeLimit, y.StartTimeLimit);
         if (rc == 0)
            rc = dtComparer.Compare (x.EndTimeLimit, y.EndTimeLimit);
         return rc;
      }
      static int SortPointsOnDate (TrackPoint x, TrackPoint y) {
         return dtComparer.Compare (x.Time, y.Time);
      }
   }

   public class Position {
      public readonly TrackAdmin Track;
      public readonly float Lat, Lon;
      public readonly float DiffInSecs;

      internal Position (TrackAdmin track, DateTime t, TrackPoint before, TrackPoint afterOrEqual) {
         Track = track;
         DiffInSecs = (float)(afterOrEqual.Time - before.Time).TotalSeconds;
         if (DiffInSecs < .1f) {
            Lat = before.Lat;
            Lon = before.Lon;
         } else {
            var fraction = (float)(t - before.Time).TotalSeconds / DiffInSecs;
            Lat = before.Lat + (afterOrEqual.Lat - before.Lat) * fraction;
            Lon = before.Lon + (afterOrEqual.Lon - before.Lon) * fraction;
         }
      }

      public override string ToString () {
         return Invariant.Format ("Track: {0}, pos={1:F4},{2:F4}", Track.Id, Lat, Lon);
      }

   }


   struct TrackPoint {
      public readonly DateTime Time;
      public readonly float Lat, Lon;
      public TrackPoint (DateTime time, float lat, float lon) {
         Time = time;
         Lat = lat;
         Lon = lon;
      }
      public TrackPoint (TrackItem item) {
         Time = item.Time;
         Lat = (float)item.Lat;
         Lon = (float)item.Lon;
      }

      public override string ToString() {
         return Invariant.Format("{0}: {1:F4},{2:F4}", Invariant.ToString(Time), Lat, Lon);
      }
   }

}

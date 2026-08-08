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

//#define DUMP_DURING_SUPERFLOUIS
//#define DUMP_DURING_DUPS

using Bitmanager.BoolParser;
using Bitmanager.Core;
using Bitmanager.Core.Collections;
using Bitmanager.Elastic;
using Bitmanager.Json;
using Bitmanager.Query;
using Bitmanager.Web;
using Bitmanager.Xml;
using System.Xml;
using static Bitmanager.AlbumTools.FaceNames;

namespace BMAlbum {

   public class PortraitSearcher : SingleFieldSearcher {
      private readonly string nestField;
      private readonly ESQuery statusFilter, positionFilter, maxFaceCountFilter;
      private readonly ESQuery minScoreFilter, minRawScoreFilter, minAreaFilter, minRatioFilter;
      private readonly int maxPhotosPerDay;
      private readonly bool delete;

      public PortraitSearcher (XmlNode node, string field, string searchField, SearchFieldConfig fieldConfig)
         : base (field, searchField.Replace('!', '.'), fieldConfig) {
         var idx = searchField.LastIndexOf('!');
         nestField = searchField.Substring(0, idx);

         statusFilter = new ESTermQuery (nestField + ".status", "v");

         int intValue;
         intValue = node.ReadInt ("@max_area_position", -1);
         if (intValue >= 0) positionFilter = new ESRangeQuery (nestField + ".index", intValue, ESRange.lte);

         float fltValue;
         fltValue = (float)node.ReadFloat ("@min_area", 0.010);
         if (fltValue > 0)
            minAreaFilter = new ESRangeQuery (nestField + ".relarea", fltValue, ESRange.gte);

         fltValue = (float)node.ReadFloat ("@min_ratio", 0.4);
         if (fltValue > 0)
            minRatioFilter = new ESRangeQuery (nestField + ".ratio_01", fltValue, ESRange.gte);

         fltValue = (float)node.ReadFloat ("@min_score", 0.4);
         if (fltValue > 0)
            minScoreFilter = new ESRangeQuery (nestField + ".match_score", fltValue, ESRange.gte);

         fltValue = (float)node.ReadFloat ("@min_raw_score", 0.4);
         if (fltValue > 0)
            minRawScoreFilter = new ESRangeQuery (nestField + ".match_score_raw", fltValue, ESRange.gte);

         intValue = node.ReadInt ("@max_face_count", 3);
         if (intValue > 0)
            maxFaceCountFilter = new ESRangeQuery ("face_count", intValue, ESRange.lte);
         maxPhotosPerDay = node.ReadInt ("@max_photos_per_day", 6);
         delete = node.ReadBool ("@delete", true);
      }


      public override ESQuery CreatePhraseQuery (QueryGenerator generator, ParserPhraseValueNode node) {
         return buildQuery (generator, node.Value);
      }

      public override ESQuery CreateRangeQuery (QueryGenerator generator, ParserRangeValueNode node) {
         return buildQuery (generator, node.Value);
      }

      public override ESQuery CreateTextQuery (QueryGenerator generator, ParserValueNode node) {
         return buildQuery(generator, node.Value);
      }

      private ESQuery buildQuery(QueryGenerator generator, string name) {
         var names = ((Settings)WebGlobals.Instance.Settings).FacesAdmin.Names;
         var resp = names.FindName(name, FindNameFlags.SortedById | FindNameFlags.SearchEqual | FindNameFlags.SearchPrefix | FindNameFlags.SearchContains);

         if (resp == null) return new ESTermQuery ("_not_found_", "_not_found_");

         if (generator is QueryGeneratorWithContext) {
            ((QueryGeneratorWithContext)generator).bitset.Set(resp.Id, true);
         }

         var bq = new ESBoolQuery();
         bq.AddFilter (new ESTermQuery (SearchField, resp.Id.ToString ()));
         bq.AddFilter (statusFilter);
         if (positionFilter != null) bq.AddFilter (positionFilter);
         if (minAreaFilter != null) bq.AddFilter (minAreaFilter);
         if (minRatioFilter != null) bq.AddFilter (minRatioFilter);
         if (minScoreFilter != null) bq.AddFilter (minScoreFilter);
         if (minRawScoreFilter != null) bq.AddFilter (minRawScoreFilter);
         var nq = new ESNestedQuery (nestField, bq, ESScoreMode.max);
         if (maxFaceCountFilter == null) return nq;
         bq = new ESBoolQuery ();
         bq.AddMust (nq);
         bq.AddFilter(maxFaceCountFilter);
         return bq;
      }


      public void PostProcess (QueryGenerator generator, List<GenericDocument> docs) {
         if (docs.Count <= 1) return;
         _docs = docs;

         var fields = createRecordFields(docs, null, out var cntWithoutDate, out var wasDateSorted); //PW null for now

         //Optional remove photos without date
         int start = 0;
         var withDate = fields.Length - cntWithoutDate;
         var ratio = withDate / (float)fields.Length;
         Logs.DebugLog.Log ("PostProcess: total={0}, withdate={1}, withoutdate={2}, ratio={3}",
            fields.Length,
            withDate,
            cntWithoutDate,
            ratio
         );
         if (ratio > .9f && cntWithoutDate < 9) {
            for (int i = 0; i < cntWithoutDate; ++i) fields[i].Deleted = true;
            start = cntWithoutDate;
            Logs.DebugLog.Log ("PostProcess: remove all non-dated photos.");
         }



         int prevDay = -1;
         int prevIndex = -1;
         for (int i = start; i < fields.Length; i++) {
            var fld = fields[i];
            if (prevDay == fld.Date) continue;
            if (prevIndex >= 0) {
               int cnt = i-prevIndex;
               if (cnt > 1) {
                  cnt -= markDeleteDups (fields, prevIndex, i);
                  if (cnt > maxPhotosPerDay && prevDay > 0 && (prevDay % 10000) != 101)
                     cnt -= markSuperflouis (fields, prevIndex, i, maxPhotosPerDay);
               }
            }
            prevIndex = i;
            prevDay = fld.Date;
         }
         if (prevIndex >= 0) {
            var cnt = fields.Length-prevIndex;
            if (cnt > 1) {
               cnt -= markDeleteDups (fields, prevIndex, fields.Length);
               if (cnt > maxPhotosPerDay && prevDay > 0 && (prevDay % 10000) != 0)
                  cnt -= markSuperflouis (fields, prevIndex, fields.Length, maxPhotosPerDay);
            }
         }

         //if (cntWithoutDate > 0) {
         //   markDeleteDups (fields, 0, cntWithoutDate, fields.Length);
         //}

         //Remove via bitset, since otherwise there is a possibility that we remove the wrong one.
         int deleted = 0;
         var bitset = new BitSet(fields.Length);
         for (int i = fields.Length; i > 0;) {
            var fld = fields[--i];
            if (fld.Deleted) {
               bitset.Set (fld.Index, true);
               ++deleted;
            }
         }
         if (deleted > 0) {
            for (int i = docs.Count; i > 0;) {
               --i;
               if (!bitset[i]) continue;
               if (delete) 
                  docs.RemoveAt (i);
               else 
                  docs[i]._Source["marked"] = true;
            }
         }

         Logs.DebugLog.Log ("PostProgress: del={0}, was sorted={1}", deleted, wasDateSorted);
         _docs = null;
      }


      static List<GenericDocument> _docs;
      private static void logDeleted (RecordFields del, RecordFields dupOf) {
         Logs.DebugLog.Log ("Removing {0}: dup with {1}", _docs[del.Index].Id, _docs[dupOf.Index].Id);
      }
      private static int markDeleteDups (RecordFields[] arr, int lo, int hi) {
#if DUMP_DURING_DUPS
         for (int i = lo; i < hi; i++) {
            var fld = arr[i];
            Logs.DebugLog.Log ("B[{0}]: del={4}, ph1={1:X8} ph2={2:X8}, id={3}", i, fld.PH1, fld.PH2, _docs[fld.Index].Id, fld.Deleted? "T" : "F");
         }
#endif
         int ret = 0;
         for (int i = lo; i < hi; i++) {
            var fld = arr[i];
            if (fld.Deleted) continue;
            for (int j = i + 1; j < hi; j++) {
               var fld2 = arr[j];
               if (fld2.Deleted) continue;
               if (fld.PH1 == fld2.PH1 && (fld.Flags & PresenceFlags.PH1) != 0) {
                  //logDeleted (fld2, fld);
                  fld2.Deleted = true;
                  ++ret;
                  continue;
               }
               if (fld.PH2 == fld2.PH2 && (fld.Flags & PresenceFlags.PH2) != 0) {
                  //logDeleted (fld2, fld);
                  fld2.Deleted = true;
                  ++ret;
                  continue;
               }
            }
         }
#if DUMP_DURING_DUPS
         for (int i = lo; i < hi; i++) {
            var fld = arr[i];
            Logs.DebugLog.Log ("A[{0}]: del={4}, ph1={1:X8} ph2={2:X8}, id={3}", i, fld.PH1, fld.PH2, _docs[fld.Index].Id, fld.Deleted ? "T" : "F");
         }
#endif
         return ret;
      }
      private static void markDeleteDups (RecordFields[] arr, int lo, int lo2, int hi2) {
         for (int i = lo; i < lo2; i++) {
            var fld = arr[i];
            if (fld.Deleted) continue;
            for (int j = lo2; j < hi2; j++) {
               var fld2 = arr[j];
               if (fld2.Deleted) continue;
               if (fld.PH1 == fld2.PH1 && (fld.Flags & PresenceFlags.PH1) != 0) {
                  //logDeleted (fld, fld2);
                  fld.Deleted = true;
                  continue;
               }
               if (fld.PH2 == fld2.PH2 && (fld.Flags & PresenceFlags.PH2) != 0) {
                  //logDeleted (fld, fld2);
                  fld.Deleted = true;
                  continue;
               }
            }
         }
      }

      class SortForSuperFlouis : IComparer<RecordFields> {
         public static readonly SortForSuperFlouis Instance = new SortForSuperFlouis();
         public int Compare (RecordFields a, RecordFields b) {
            if (a.Deleted != b.Deleted)
               return a.Deleted ? -1 : 1;
            return b.Difference - a.Difference;
            //return a.FaceCount - b.FaceCount;
         }
      }

      private static int markSuperflouis (RecordFields[] fields, int lo, int hi, int maxPhotosPerDay) {
         for (int i = lo; i < hi; i++) fields[i].Difference = 0;
#if DUMP_DURING_SUPERFLOUIS
         for (int i = lo; i < hi; i++) {
            var fld = fields[i];
            Logs.DebugLog.Log ("B[{0}]: del={4}, ph1={1:X8} ph2={2:X8}, diff={5}, id={3}", i, fld.PH1, fld.PH2, _docs[fld.Index].Id, fld.Deleted ? "T" : "F", fld.Difference);
         }
#endif
         for (int i = lo; i < hi; i++) { 
            var fld = fields[i];
            for (int j = i+1; j < hi; j++) {
               var fld2 = fields[j];
               var d = Bits.NumOneBits(fld.PH1 ^ fld2.PH1) + Bits.NumOneBits(fld.PH2 ^ fld2.PH2);
               if (d > fld.Difference) fld.Difference = d;
               if (d > fld2.Difference) fld2.Difference = d;
            }
         }
         Array.Sort (fields, lo, hi - lo, SortForSuperFlouis.Instance);
         for (int i = lo + maxPhotosPerDay; i < hi; i++) fields[i].Deleted = true;

#if DUMP_DURING_SUPERFLOUIS
         for (int i = lo; i < hi; i++) {
            var fld = fields[i];
            Logs.DebugLog.Log ("A[{0}]: del={4}, ph1={1:X8} ph2={2:X8}, diff={5}, id={3}", i, fld.PH1, fld.PH2, _docs[fld.Index].Id, fld.Deleted ? "T" : "F", fld.Difference);
         }
#endif
         return hi - lo - maxPhotosPerDay;
      }

      private static RecordFields[] createRecordFields (List<GenericDocument> docs, QueryGeneratorWithContext context, out int noDateCount, out bool _sortedOnDate) {
         var bitset = context?.bitset;
         var ret = new RecordFields[docs.Count];
         int noDateCnt = 0;
         int prevDate = -1;
         bool sortedOnDate = true;
         for (int i = 0; i < docs.Count; ++i) {
            ret[i] = new RecordFields (docs[i], i, bitset);
            var date = ret[i].Date;
            if (date < prevDate) sortedOnDate = false;
            prevDate = date;
            if (date < 1000 * 10000) noDateCnt++;
         }
         noDateCount = noDateCnt;
         _sortedOnDate = sortedOnDate;

         if (!sortedOnDate) {
            Array.Sort (ret, (a, b) => a.Date - b.Date);
         }
         return ret;
      }

      enum PresenceFlags : byte { Year =1, MMDD=2, PH1=4, PH2=8};
      class RecordFields {
         public readonly int PH1, PH2;
         public readonly int Index;
         public readonly int FaceCount;
         public readonly int Date;
         public readonly float Score, Ratio, Area;
         public int Difference;
         public readonly PresenceFlags Flags;
         public bool Deleted;
         public RecordFields (GenericDocument doc, int index, BitSet bitset) {
            Index = index;
            var rec = doc._Source;
            FaceCount = rec.ReadInt ("face_count", 0);
            JsonValue v;
            if (rec.TryGetValue ("date", out v)) {
               var dt = v.AsDateTime();
               if (dt != DateTime.MinValue) {
                  dt = dt.ToLocalTime ();
                  Flags |= PresenceFlags.Year;
                  Date = dt.Year * 10000 + dt.Month * 100 + dt.Day;
                  if (Date % 10000 != 0) Flags |= PresenceFlags.MMDD;
               }
            }
            if (rec.TryGetValue ("ph1", out v)) {
               PH1 = v.AsInt();
               Flags |= PresenceFlags.PH1;
            }
            if (rec.TryGetValue ("ph2", out v)) {
               PH2 = v.AsInt ();
               Flags |= PresenceFlags.PH2;
            }
            if (bitset != null) {
               var names = rec.ReadArr("names", null);
               if (names != null && names.Count > 0) {
                  for (int i=0; i<names.Count; i++) {
                     var name = (JsonObjectValue)names[i];
                     int id = name.ReadInt("id");
                     if (!bitset[id]) continue;
                     var score = name.ReadFloat("match_score_raw");
                     if (score > Score) Score = score;
                     var ratio = name.ReadFloat("ratio_01");
                     if (ratio > Ratio) Ratio = ratio;
                     var area = name.ReadFloat("relarea");
                     if (area > Area) Area = area;
                  }

               }
            }
         }
      }
   }


   public class QueryGeneratorWithContext: QueryGenerator {
      internal readonly BitSet bitset; 
      public QueryGeneratorWithContext(Settings settings, SearchSettings searchSettings, ESIndexInfo indexInfo, string q)
         : base(searchSettings, indexInfo, q) {
         bitset = new BitSet (0);
      }
   }
}

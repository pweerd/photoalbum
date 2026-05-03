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

using Bitmanager.AlbumTools;
using Bitmanager.Core;
using Bitmanager.Elastic;
using Bitmanager.IO;
using Bitmanager.Json;
using System.Text;

namespace AlbumImporter.FaceRecognition {
   public class FaceStatistics {
      private readonly Logger logger;
      private readonly FaceNames faceNames;
      private readonly string reportDir;

      //Old values
      private FaceForStats[] oldFaces;
      private int[] oldCtr_A;
      private int[] oldCtr_C;
      private int[] oldCtr_M;
      private bool useAllFaces;
      private readonly int reportGenerations;

      public FaceStatistics (Logger logger, FaceNames faceNames, string reportDir) 
         :this (logger, faceNames, reportDir, 5) {
      }
      public FaceStatistics (Logger logger, FaceNames faceNames, string reportDir, int generations) { 
         this.logger = logger;
         this.faceNames = faceNames;
         this.reportDir = reportDir;
         this.reportGenerations = generations;
      }

      public void LoadExisting (IndexInfo index, bool all) {
         oldCtr_M = countNameUsage (index, "m");
         oldCtr_A = countNameUsage (index, "a");
         oldCtr_C = countNameUsage (index, "c");
         oldFaces = readFaces (index, all);
         useAllFaces = all;
      }


      private int[] countNameUsage (IndexInfo index, string src) {
         int[] ret = new int[faceNames.Count];
         if (index == null) return ret;

         var req = index.CreateESRequest();
         req.TrackTotalHits = true;
         var bq = new ESBoolQuery();
         req.Query = bq;
         bq.AddFilter (new ESTermQuery ("src", "k"));
         bq.AddFilter (new ESTermQuery ("src", src));
         var termsAgg = new ESTermsAggregation ("nameid", "names.id", faceNames.Count+1);
         var nestedAgg = new ESNestedAggregation ("nameid", "names", termsAgg);
         req.Size = 0;
         req.Aggregations.Add (nestedAgg);
         var resp = req.Search ();
         resp.ThrowIfError ();

         var terms = (ESTermsAggregationResult)resp.Aggregations.FindByName (true, "nameid", "nameid");
         foreach (var item in terms.GetSortedItems ()) {
            int id = Invariant.ToInt32 (item.GetKey ());
            if (id >= 0) ret[id] = item.Count;
         }
         return ret;
      }

      public void DumpNameUsage (IndexInfo index) {
         if (index == null) return;
         var ctr_M = countNameUsage (index, "m");
         var ctr_A = countNameUsage (index, "a");
         var ctr_C = countNameUsage (index, "c");

         if (oldCtr_A==null) {
            oldCtr_A = ctr_A;
            oldCtr_C = ctr_C;
            oldCtr_M = ctr_M;
         }

         var sortHelper = new Tuple<int,int>[ctr_M.Length];
         for (int i = 0; i < ctr_M.Length; i++) {
            sortHelper[i] = new Tuple<int, int> (i, ctr_M[i]);
         }
         Array.Sort (sortHelper, (a, b) => b.Item2 - a.Item2);

         var sb = new StringBuilder ();
         int total_M = 0;
         int total_A = 0;
         int totalZeroes = 0;
         for (int idx = 0; idx < sortHelper.Length; idx++) {
            int i = sortHelper[idx].Item1;

            var diffManual = ctr_M[i] != oldCtr_M [i] || ctr_C[i] != oldCtr_C [i];
            var diff = diffManual || ctr_A[i] != oldCtr_A [i];
            if (ctr_M[i] == 0) ++totalZeroes;
            string lbl;
            if (diffManual) {
               lbl = "NE_M";
               ++total_M;
            } else if (diff) {
               lbl = "NE_A";
               ++total_A;
            } else lbl = "    ";
               sb.AppendFormat ("{0} M={1} ({2}), C={3} ({4}) A={5} ({6}) name={7}\n",
                  lbl,
                  ctr_M[i], oldCtr_M[i],
                  ctr_C[i], oldCtr_C[i],
                  ctr_A[i], oldCtr_A[i],
                  faceNames.NameById (i)
               );
         }
         sb.AppendFormat ("Total #no_manual={0}, #diffs_in_manual={1}, #diffs_in_auto={2}\n", totalZeroes, total_M, total_A);

         int fromLen=sb.Length;
         for (int idx = 0; idx < sortHelper.Length; idx++) {
            int i = sortHelper[idx].Item1;
            if (ctr_M[i] > 1 || ctr_M[i]>=oldCtr_M[i]) continue;

            if (fromLen == sb.Length)
               sb.Append ("\n\nFollowing names dropped below a critical #manual assigned:\n");
            sb.AppendFormat ("CRIT M={0} ({1}), name={2}\n",
               ctr_M[i], oldCtr_M[i],
               faceNames.NameById (i)
            );
         }

         var fg = new FileGenerations2(Path.Combine(reportDir, "face-name-stats"), ".txt", reportGenerations);
         var fn = fg.CreateTargetName ();
         File.WriteAllText (fn, sb.ToString ());
         fg.RemoveSuperflouisGenerations ();

         var lt = total_M+total_A > 0 ? _LogType.ltWarning : _LogType.ltInfo;
         logger.Log (lt, "Face usage statistics: Total #no_manual={0}, #diffs_in_manual={1}, #diffs_in_auto={2}", totalZeroes, total_M, total_A);
         logger.Log (lt, "-- Please check file [{0}] for details.", fn);
      }

      public void DumpNameUsage (IndexInfo index, IndexInfo oldIndex, bool assignedToo) {
         LoadExisting (oldIndex, assignedToo);
         DumpNameUsage (index);
      }
      public void DumpDifferences (IndexInfo index, IndexInfo oldIndex, bool assignedToo) {
         LoadExisting (oldIndex, assignedToo);
         DumpDifferences (index);
      }

      public void DumpDifferences (IndexInfo index) {
         if (index == null || oldFaces == null) return;
         var faces = readFaces(index, useAllFaces);
         var list_M = new List<string>();
         var list_A = new List<string>();
         List<string> list;
         string lbl;
         int changed_M = 0, changed_A=0;
         int missed_M = 0, missed_A=0;

         void initChgLabelAndList (FaceForStats face) {
            if ((face.Src & NameSource.Manual) != 0) {
               lbl = "NE_M";
               list = list_M;
               ++changed_M;
            } else if ((face.Src & NameSource.Corrected) != 0) {
               lbl = "NE_C";
               list = list_M;
               ++changed_M;
            } else {
               lbl = "NE_A";
               list = list_A;
               ++changed_A;
            }
         }
         void initMissLabelAndList (FaceForStats face) {
            if ((face.Src & NameSource.Manual) != 0) {
               lbl = "MS_M";
               list = list_M;
               ++missed_M;
            } else if ((face.Src & NameSource.Corrected) != 0) {
               lbl = "MS_C";
               list = list_M;
               ++missed_M;
            } else {
               lbl = "MS_A";
               list = list_A;
               ++missed_A;
            }
         }

         int i = 0;
         int j = 0;
         while (i < oldFaces.Length && j < faces.Length) {
            int cmp = string.CompareOrdinal(oldFaces[i].Id, faces[j].Id);
            if (cmp==0) {
               if (oldFaces[i].NameId != faces[j].NameId) {
                  initChgLabelAndList (oldFaces[i]);
                  list.Add (Invariant.Format ("{0} {1}: src={2}, {3}->{4}",
                     lbl,
                     oldFaces[i].Id,
                     oldFaces[i].Src,
                     faceNames.NameById (oldFaces[i].NameId),
                     faceNames.NameById (faces[j].NameId)
                  ));
               }
               ++i;
               ++j;
               continue;
            }
            if (cmp < 0) { //ID is missing in newList
               initMissLabelAndList (oldFaces[i]);
               list.Add (Invariant.Format ("{0} {1}: src={2}, name={3}",
                  lbl,
                  oldFaces[i].Id,
                  oldFaces[i].Src,
                  faceNames.NameById (oldFaces[i].NameId)
               ));
               ++i;
               continue;
            }
            ++j;
         }
         for (; i < oldFaces.Length; i++) {
            initMissLabelAndList (oldFaces[i]);
            list.Add (Invariant.Format ("{0} {1}: src={2}, name={3}",
               lbl,
               oldFaces[i].Id,
               oldFaces[i].Src,
               faceNames.NameById (oldFaces[i].NameId)
            ));
            ++i;
         }

         string txt_M = Invariant.Format ("Differences in important faces (manual/corrected): #missing={0}, #changed={1}", missed_M, changed_M);
         string txt_A = Invariant.Format ("Differences in automatic assigned faces: #missing={0}, #changed={1}", missed_A, changed_A);

         var fg = new FileGenerations2(Path.Combine(reportDir, "face-diffs"), ".txt", reportGenerations);
         var fn = fg.CreateTargetName ();
         using (var fs = IOUtils.CreateOutputStream (fn)) {
            var wtr = fs.CreateTextWriter();
            wtr.WriteLine (txt_M);
            foreach (var line in list_M) wtr.WriteLine(line);
            wtr.Write ("\n\n");
            wtr.WriteLine (txt_A);
            foreach (var line in list_A) wtr.WriteLine (line);
            wtr.Close ();
         }
         fg.RemoveSuperflouisGenerations ();

         var lt = missed_A + missed_M + changed_A + changed_M > 0 ? _LogType.ltWarning : _LogType.ltInfo;
         logger.Log (lt, "Face differences:");
         logger.Log (lt, "-- " + txt_M);
         logger.Log (lt, "-- " + txt_A);
         logger.Log (lt, "-- Please check file [{0}] for details.", fn);
      }

      private FaceForStats[] readFaces (IndexInfo index, bool all) {
         if (index == null) return null;
         var req = index.CreateESRequest();
         req.TrackTotalHits = true;
         req.Query = all ? new ESExistsQuery("src") : new ESTermsQuery ("src", "m", "c");
         req.SetSource("src;names", null);

         var list = new List<FaceForStats> (16000);
         using (var records = new ESRecordEnum (req)) {
            foreach (var rec in records) {
               list.Add (new FaceForStats (rec));
            }
         }
         var ret = list.ToArray ();
         Array.Sort (ret, FaceForStats.SortOnId);
         return ret;
      }
   }

   struct FaceForStats {
      public readonly string Id;
      public readonly int NameId;
      public readonly NameSource Src;

      public FaceForStats (GenericDocument doc) {
         this.Id = doc.Id;
         var names = doc._Source.ReadArr("names", null);
         NameId = names == null ? -1 : ((JsonObjectValue)names[0]).ReadInt ("id");
         Src = NameSourceExtensions.FromString (doc.ReadStr ("src", null));
      }

      public static int SortOnId (FaceForStats a, FaceForStats b) {
         return string.CompareOrdinal(a.Id, b.Id);
      }
   }
}

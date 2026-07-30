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

using Bitmanager.BoolParser;
using Bitmanager.Core;
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
      private readonly string scoreField;
      private readonly string rawScoreField;
      private readonly string ratioField;
      private readonly string areaField;
      private readonly FacesAdmin facesAdmin;
      private readonly float minArea, minRatio, minScore, minRawScore;
      private readonly int maxFaceCount;

      public PortraitSearcher (XmlNode node, string field, string searchField, SearchFieldConfig fieldConfig)
         : base (field, searchField.Replace('!', '.'), fieldConfig) {
         var idx = searchField.LastIndexOf('!');
         nestField = searchField.Substring(0, idx);
         scoreField = nestField + ".match_score";
         rawScoreField = nestField + ".match_score_raw";
         ratioField = nestField + ".ratio_01";
         areaField = nestField + ".relarea";
         minArea = (float)node.ReadFloat ("@min_area", 0.010);
         minRatio = (float)node.ReadFloat ("@min_ratio", 0.4);
         minScore = (float)node.ReadFloat ("@min_score", 0.4);
         minRawScore = (float)node.ReadFloat ("@min_raw_score", 0);
         maxFaceCount = node.ReadInt ("@max_face_count", 2);
         facesAdmin = ((Settings)WebGlobals.Instance.Settings).FacesAdmin;
      }

     
      public override ESQuery CreatePhraseQuery (QueryGenerator generator, ParserPhraseValueNode node) {
         return buildQuery (node.Value);
      }

      public override ESQuery CreateRangeQuery (QueryGenerator generator, ParserRangeValueNode node) {
         return buildQuery (node.Value);
      }

      public override ESQuery CreateTextQuery (QueryGenerator generator, ParserValueNode node) {
         return buildQuery(node.Value);
      }

      private ESQuery buildQuery(string name) {
         var names = ((Settings)WebGlobals.Instance.Settings).FacesAdmin.Names;
         var resp = names.FindName(name, FindNameFlags.SortedById | FindNameFlags.SearchEqual | FindNameFlags.SearchPrefix | FindNameFlags.SearchContains);

         if (resp == null) return new ESTermQuery ("_not_found_", "_not_found_");
         var bq = new ESBoolQuery();
         bq.AddMust (new ESTermQuery (SearchField, resp.Id.ToString ()));
         if (minArea > 0) bq.AddFilter (new ESRangeQuery (areaField, minArea, ESRange.gte));
         if (minRatio > 0) bq.AddFilter (new ESRangeQuery (ratioField, minRatio, ESRange.gte));
         if (minScore > 0) bq.AddFilter (new ESRangeQuery (scoreField, minScore, ESRange.gte));
         if (minRawScore > 0) bq.AddFilter (new ESRangeQuery (rawScoreField, minRawScore, ESRange.gte));
         var nq = new ESNestedQuery (nestField, bq, ESScoreMode.max);
         if (maxFaceCount <= 0) return nq;
         bq = new ESBoolQuery ();
         bq.AddMust (nq);
         bq.AddFilter(new ESRangeQuery ("face_count", maxFaceCount, ESRange.lte));
         return bq;
      }


      public static void PostProcess (List<GenericDocument> docs) {
         JsonObjectValue prev = null;
         long prevHash1 = 0;
         long prevHash2 = 0;
         for (int i=docs.Count-1; i>= 0; --i) {
            var rec = docs[i]._Source;
            var hash1 = rec.ReadLong("ph1", 0);
            var hash2 = rec.ReadLong("ph2", 0);
            //Logs.DebugLog.Log ("Line {0}: {1:X8}, {2:X8}", i, hash1, hash2);
            if (prev != null) {
               if ((prevHash1 == hash1 && hash1 != 0) || (prevHash2 == hash2 && hash2 != 0)) {
                  docs.RemoveAt (i);
                  continue;
               }
            }
            prev = rec;
            prevHash1 = hash1;
            prevHash2 = hash2;
         }
      }
   }
}

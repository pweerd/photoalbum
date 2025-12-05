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
using Bitmanager.Elastic;
using Bitmanager.Json;
using Bitmanager.Query;
using Bitmanager.Web;
using Bitmanager.Xml;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace BMAlbum.Core.Searchers {
   public class PHashSearcher : SingleFieldSearcher {
      private static readonly JsonArrayValue stopWords;
      private static readonly JsonArrayValue fields;

      public PHashSearcher(XmlNode node, string field, string searchField, SearchFieldConfig fieldConfig)
             : base (field, searchField, fieldConfig) {
      }


      private static Regex hashExpr = new Regex("^(?:[0,1][0..9,A..F]{2} ?)+$", RegexOptions.Compiled);

      private ESQuery createTermQ (string fld, string v) {
         return new ESConstantScoreQuery(new ESTermQuery(fld, v));
      }
      private ESQuery createMltQuery (string q) {
         var settings = (Settings)WebGlobals.Instance.Settings;
         var req = settings.ESClient.CreateSearchRequest (settings.MainIndex);
         req.Size = 1;
         //req.SetSource ("location,album", null);
         req.Query = new ESIdsQuery (null, q);

         var resp = req.Search ();
         resp.ThrowIfError ();

         if (resp.Documents.Count == 0) return null;
         string arg = resp.Documents[0].ReadStr (SearchField, null);
         if (arg == null) return null;

         var mtpq = new ESBoolQuery(32);
         int prevIdx = -1;
         int i;
         for (i=0; i<arg.Length; i++) {
            if (arg[i] != ' ') {
               if (prevIdx < 0) prevIdx = i;
               continue;
            }
            if (i-prevIdx>=2) {
               mtpq.AddShould(createTermQ(SearchField, arg.Substring(prevIdx, i - prevIdx)));
            }
            prevIdx = -1;
         }
         if (prevIdx >=0 && prevIdx < i)
            mtpq.AddShould(createTermQ(SearchField, arg.Substring(prevIdx, i - prevIdx)));

         mtpq.SetBoost (1f/32);
         return mtpq;
      }
      public override ESQuery CreatePhraseQuery (QueryGenerator generator, ParserPhraseValueNode node) {
         return createMltQuery (node.Value);
      }

      public override ESQuery CreateRangeQuery (QueryGenerator generator, ParserRangeValueNode node) {
         throw new NotImplementedException ();
      }

      public override ESQuery CreateTextQuery (QueryGenerator generator, ParserValueNode node) {
         return createMltQuery (node.Value);
      }

   }
}

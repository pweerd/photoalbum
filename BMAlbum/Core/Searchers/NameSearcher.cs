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
using Bitmanager.Query;
using Bitmanager.Xml;
using System.Xml;

namespace BMAlbum {

   public class NameSearcher : SingleFieldSearcher {
      private readonly string nestField;
      private readonly string scoreField;
      private readonly string positionField;
      private readonly ESQuery validFilter;
      private readonly bool onlyValid;

      public NameSearcher (XmlNode node, string field, string searchField, SearchFieldConfig fieldConfig)
         : base (field, searchField.Replace('!', '.'), fieldConfig) {
         var idx = base.SearchField.LastIndexOf('.');
         string rootField = null;
         if (idx < 0) {
            scoreField = "match_score";
            positionField = "index";
         } else {
            rootField = base.SearchField.Substring (0, idx);
            if (searchField[idx] == '!') nestField = rootField;
            scoreField = rootField + ".match_score";
            positionField = rootField + ".index";
         }
         onlyValid = node.ReadBool("@only_valid", true);
         if (onlyValid && nestField != null) validFilter = new ESTermQuery (rootField + ".status", "v");
      }

      private ESQuery wrapNestedScorer (ESQuery q, int pos) {
         if (validFilter == null && pos < 0) {
            q = new ESConstantScoreQuery (q);
         } else {
            var bq = new ESBoolQuery();
            if (validFilter != null) bq.AddFilter (validFilter);
            if (pos >= 0) bq.AddFilter(new ESTermQuery(positionField, pos.ToString()));
            bq.AddFilter (q);
            q = bq;
         }

         var fq = new ESFunctionScoreQuery (q);
         fq.BoostMode = ESBoostMode.replace;
         fq.Functions.Add (new ESFieldScoreFunction (scoreField));

         return nestField == null ? fq : new ESNestedQuery (nestField, fq, ESScoreMode.max);
      }
      
      public override ESQuery CreatePhraseQuery (QueryGenerator generator, ParserPhraseValueNode node) {
         var arg = splitNameAndPosition (node.Value, out var pos);
         if (generator.IndexInfo.GetField (positionField, false) == null) pos = -1;
         return wrapNestedScorer (base.CreatePhraseQuery (generator, arg), pos);
      }

      public override ESQuery CreateRangeQuery (QueryGenerator generator, ParserRangeValueNode node) {
         return wrapNestedScorer(base.CreateRangeQuery (generator, node), -1);
      }

      public override ESQuery CreateTextQuery (QueryGenerator generator, ParserValueNode node) {
         var arg = splitNameAndPosition (node.Value, out var pos);
         if (generator.IndexInfo.GetField (positionField, false) == null) pos = -1;
         return wrapNestedScorer (base.CreateTextQuery (generator, arg), pos);
      }

      private static string splitNameAndPosition (string arg, out int position) {
         if (string.IsNullOrEmpty (arg)) goto NO_POSITION;
         var x = arg.LastIndexOf('@');
         if (x<0 || (arg.Length - x) < 2) goto NO_POSITION;

         int pos = 0;
         for (int j=x+1; j<arg.Length; j++) {
            int ch = arg[j] - '0';
            if (ch < 0) goto NO_POSITION;
            if (ch > 9) goto NO_POSITION;
            pos = 10 * pos + ch;
         }
         position = pos;
         return arg.Substring (0, x);

      NO_POSITION:
         position = -1;
         return arg;
      }
   }

}

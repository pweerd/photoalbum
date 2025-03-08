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
using Bitmanager.Webservices;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using System.Diagnostics;

namespace BMAlbum {

   public class NameSearcher : SingleFieldSearcher {

      public NameSearcher (string field, string searchField, SearchFieldConfig fieldConfig)
         : base (field, searchField.Replace('!', '.'), fieldConfig) {
      }
      
      private ESQuery wrapNestedScorer (ESQuery q) {
         var fq = new ESFunctionScoreQuery (q);
         fq.BoostMode = ESBoostMode.replace; 
         fq.Functions.Add (new ESFieldScoreFunction ("names.match_score"));
         return new ESNestedQuery ("names", fq, ESScoreMode.max);
      }
      public override ESQuery CreatePhraseQuery (QueryGenerator generator, ParserPhraseValueNode node) {
         return wrapNestedScorer (base.CreatePhraseQuery (generator, node));
      }

      public override ESQuery CreateRangeQuery (QueryGenerator generator, ParserRangeValueNode node) {
         return wrapNestedScorer(base.CreateRangeQuery (generator, node));
      }

      public override ESQuery CreateTextQuery (QueryGenerator generator, ParserValueNode node) {
         return wrapNestedScorer (base.CreateTextQuery (generator, node));
      }

   }
}

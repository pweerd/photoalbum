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
using System.Xml;

namespace BMAlbum.Core.Searchers {
   public class MoreLikeThisSearcher : SingleFieldSearcher {
      private readonly string idField;
      private static readonly JsonArrayValue stopWords;
      private static readonly JsonArrayValue fields;

      public MoreLikeThisSearcher (XmlNode node, string field, string searchField, SearchFieldConfig fieldConfig)
             : base (field, searchField, fieldConfig) {
         idField = node.ReadStr ("@id_field", "file");
      }

      static MoreLikeThisSearcher() {
         stopWords = new JsonArrayValue ("een", 
            "het", 
            "naar", 
            "van", 
            "met", 
            "uit",
            "bij",
            "was",
            "hij",
            "zij",
            "wij",
            "dan",
            "dit",
            "die",
            "als",
            "door",
            "naar",
            "over"
         );
         fields = new JsonArrayValue ((JsonValue)"all");
      }

      private ESQuery createMltQuery (string q) {
         var settings = (Settings)WebGlobals.Instance.Settings;
         var req = settings.ESClient.CreateSearchRequest (settings.MainIndex);
         req.Size = 1;
         //req.SetSource ("location,album", null);
         req.Query = new ESIdsQuery (null, q);

         var resp = req.Search ();
         resp.ThrowIfError ();

         var sb = new StringBuilder();
         if (resp.Documents.Count>0) {
            sb.Append (resp.Documents[0].ReadStr ("text_nl", null));
            string text = resp.Documents[0].ReadStr ("text", null);
            if (text.Contains("_OCR_V_")) sb.Append (' ').Append ("_OCR_V_");
         }

         var json = new JsonObjectValue ("fields", fields);
         json.Add ("like", sb.ToString ());
         json.Add ("min_word_length", 3);
         json.Add ("min_term_freq", 0);
         json.Add ("stop_words", stopWords);
         json = new JsonObjectValue ("more_like_this", json);

         var mtpq = new ESJsonQuery (json);
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

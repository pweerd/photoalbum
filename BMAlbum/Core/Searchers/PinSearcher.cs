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
using Bitmanager.Query;
using Bitmanager.Web;
using Bitmanager.Xml;
using System.Text.RegularExpressions;
using System.Xml;

namespace BMAlbum {
   public class PinSearcher : SearcherBase {
      private static ESQuery NO_RESULT = new ESTermQuery ("_not_exist_", "_not_exist_");
      private static readonly Logger logger = WebGlobals.Instance.SiteLog.Clone ("pin_searcher", _LogType.ltWarning);
      private static readonly Regex latlonExpr = new Regex (@"^\-?[\d\.]+[, ]+[\d\.]+$", RegexOptions.Compiled);
      private readonly string defDistance;
      private readonly string idField;
      private readonly ESQuery existQuery;
      private static readonly ESSortField sortDate = new ESSortField ("sort_key", ESSortDirection.desc);
      private static readonly ESQuery existLocaion = new ESExistsQuery ("location");

      public PinSearcher (XmlNode node, string field, string searchField, SearchFieldConfig cfg)
         : base (field, searchField, cfg) {
         defDistance = node.ReadInt ("@def_distance", 20) + "km";
         idField = node.ReadStr ("@id_field", "file");
         existQuery = SearchField=="location" ? existLocaion : new ESExistsQuery (SearchField);
      }

      public override ESQuery CreatePhraseQuery (QueryGenerator generator, ParserPhraseValueNode node) {
         return CreateLocationQuery (generator, node.Value);
      }

      public override ESQuery CreateRangeQuery (QueryGenerator generator, ParserRangeValueNode node) {
         throw new NotImplementedException ();
      }

      public override ESQuery CreateTextQuery (QueryGenerator generator, ParserValueNode node) {
         return CreateLocationQuery (generator, node.Value);
      }

      protected virtual ESQuery CreateLocationQuery (QueryGenerator generator, string strLoc) {
         var pin = Pin.Create (strLoc);
         if (pin.Position == null) return pin.IdQuery;

         var dist = pin.Distance ?? defDistance;
         ESQuery q = new ESGeoDistanceQuery (SearchField, pin.Position, dist);

         //Score by distance
         var fsq = new ESFunctionScoreQuery (q);
         q = fsq;
         fsq.BoostMode = ESBoostMode.replace;

         var func = new ESScoreFunctionDecay (ESScoreFunctionDecay.DecayFunctionType.exp, SearchField);
         func.Scale = dist;
         func.Origin = pin.Position;
         func.Decay = 0.0001;
         fsq.Functions.Add (func);

         if (pin.IdQuery != null) q = new ESBoolQuery ().AddMust (q).AddShould (pin.IdQuery);
         return q.SetBoost (Weight);
      }
   }
}

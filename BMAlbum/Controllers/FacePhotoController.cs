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
using Bitmanager.Http;
using Bitmanager.Json;
using Bitmanager.Query;
using Bitmanager.Web;
using Microsoft.AspNetCore.Mvc;
using System.Drawing;

namespace BMAlbum.Controllers {
   public class FacePhotoController : BaseController {
      public IActionResult Index () {
         var settings = (Settings)base.Settings;
         var faceNames = settings.FacesAdmin.Names;
         var clientState = new ClientState (RequestCtx, settings);
         var c = settings.ESClient;
         var req = c.CreateSearchRequest (settings.FaceIndex);
         clientState.Sort.ToSearchRequest (req);
         //req.Sort.Add (new ESSortField ("names.id", ESSortDirection.desc, "names"));
         //if (req.Sort.Count == 1) clientState.Sort = settings.FaceSearchSettings.SortModes[0];
         req.TrackTotalHits = true;
         req.Size = 1000;
         var bq = new ESBoolQuery ();
         req.Query = bq;
         bq.AddFilter (new ESExistsQuery ("count"));

         QueryGenerator queryGenerator = null;
         if (clientState.Query != null) {
            queryGenerator = new QueryGenerator (settings.FaceSearchSettings, settings.IndexInfoCache.GetIndexInfo (settings.FaceIndex), clientState.Query);
            if (queryGenerator.ParseResult.Root is ParserEmptyNode) queryGenerator = null;
            else {
               bq.AddMust (queryGenerator.GenerateQuery (FuzzyMode.None).WrapNestedQueries(ESScoreMode.max));
            }
         }

         var resp = req.Search ();
         resp.ThrowIfError ();

         if (resp.Documents.Count>0) {
            settings.FacesAdmin.GetStorage (resp.Documents[0].Index);
         }

         var json = new JsonMemoryBuffer ();
         json.WriteStartObject ();
         json.WriteProperty ("chgid", faceNames.ChangeId);
         json.WriteProperty ("total", resp.TotalHits);
         json.WriteProperty ("new_state", (IJsonSerializable)clientState.ToJson ());
         if ((clientState.DebugFlags & DebugFlags.TRUE) != 0) {
            json.WriteStartObject ("dbg");
            //json.WriteStartArray ("timings");
            //foreach (var t in timings) json.WriteValue (t);
            //json.WriteEndArray ();
            json.WriteProperty ("es_request", req);
            json.WriteEndObject ();
         }

         json.WriteStartArray ("files");
         foreach (var doc in resp.Documents) {
            var src = doc._Source;
            src["id"] = doc.Id;
            src.WriteTo (json);
         }
         json.WriteEndArray ();
         json.WriteEndObject ();
         return new JsonActionResult (json);
      }


      private static void writeName(JsonMemoryBuffer json, int id, string name) {
         json.WriteStartObject ();
         json.WriteProperty ("id", id);
         json.WriteProperty ("name", name);
         json.WriteEndObject ();
      }
      public IActionResult Names () {
         var settings = (Settings)base.Settings;
         var faceNames = settings.FacesAdmin.Names;
         var json = new JsonMemoryBuffer ();
         json.WriteStartObject ();
         json.WriteProperty ("chgid", faceNames.ChangeId);
         json.WriteStartArray ("names");
         writeName (json, -2, "CLEAR");
         writeName (json, -1, "ERROR");
         var names = faceNames.SortedNames;
         for (int i=0; i< names.Length; i++) 
            writeName (json, names[i].Id, names[i].Name);
         json.WriteEndArray ();
         json.WriteEndObject ();
         return new JsonActionResult (json);
      }

      public IActionResult Get (string storId) {
         var settings = (Settings)base.Settings;
         var storage = settings.FacesAdmin.GetStorage();
         if (storage == null) goto NOT_FOUND;
         var entry = storage.GetFileEntry (storId);
         if (entry == null) goto NOT_FOUND;

         byte[] buf;
         lock (storage) {
            buf = storage.GetBytes (entry);
         }
         return new BytesActionResult ("image/jpeg", buf);

         NOT_FOUND: return new ActionResult404 ();
      }

      private const int ID_MIN = -2;
      private const int ID_CLEAR = -2;
      private const int ID_ERROR = -1;
      public IActionResult SetFace (string id, string faceId) {
         var settings = (Settings)base.Settings;
         var names = settings.FacesAdmin.Names;
         int idx = Invariant.ToInt32 (faceId);
         if (idx < ID_MIN || idx > names.Count)
            throw new BMException ("Invalid faceId [{0}]. Face-count={1}.", idx, names.Count);
         var c = settings.ESClient;
         var idUrl = settings.FaceIndex + "/_doc/" + Encoders.UrlDataEncode (id);
         var rec = c.SendGetJson (HttpMethod.Get, idUrl);
         rec = rec.ReadObj ("_source");
         SiteLog.Log ("Update id=" + id);
         SiteLog.Log ("-- Old record=" + rec.ToJsonString ());
         switch (idx) {
            case ID_CLEAR: 
               rec.Remove ("names");
               rec["src"] = "U";
               break;
            case ID_ERROR: 
               rec.Remove ("names");
               rec["src"] = "E";
               break;
            default: //Update
               rec["src"] = "M";
               var nameObj = new JsonObjectValue ();
               rec["names"] = new JsonArrayValue ((JsonValue)nameObj);
               nameObj["id"] = idx;
               nameObj["match_score"] = 1;
               nameObj["name"] = names.NameById (idx);
               break;
         }
         SiteLog.Log ("-- New record=" + rec.ToJsonString ());
         c.Send (HttpMethod.Put, idUrl, HttpPayload.Create (rec)).ThrowIfError ();
         return new JsonActionResult ();
      }

      public IActionResult ClearAutoFaces () {
         var settings = (Settings)base.Settings;
         var c = settings.ESClient;
         var req = c.CreateSearchRequest(settings.FaceIndex);
         req.Query = new ESTermQuery ("src", "A");
         var recs = new ESRecordEnum (req);
         int cnt=0;
         foreach (var d in recs) {
            var idUrl = settings.FaceIndex + "/_doc/" + Encoders.UrlDataEncode (d.Id);
            var json = d._Source;
            json.Remove ("names");
            json["src"] = "U";
            c.Send (HttpMethod.Put, idUrl, HttpPayload.Create (json)).ThrowIfError ();
            cnt++;
         }
         var resp = new JsonObjectValue ("cleared", cnt);
         return new JsonActionResult (resp);
      }

   }
}

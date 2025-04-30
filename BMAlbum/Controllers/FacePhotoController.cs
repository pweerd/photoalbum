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
using BMAlbum.Core;
using Microsoft.AspNetCore.Mvc;

namespace BMAlbum.Controllers {
   public class FacePhotoController : BaseController {
      public IActionResult Index () {
         var settings = (Settings)base.Settings;
         var faceNames = settings.FacesAdmin.Names;
         var clientState = new ClientState (RequestCtx, settings);
         if (!clientState.InternalIp) return new JsonActionResult ();

         var c = settings.ESClient;
         var req = c.CreateSearchRequest (settings.FaceIndex);
         clientState.Sort.ToSearchRequest (req);
         req.Size = 1000;
         var bq = new ESBoolQuery ();
         req.Query = bq;
         bq.AddFilter (new ESTermQuery ("any_face", "true"));

         QueryGenerator queryGenerator = null;
         if (clientState.Query != null) {
            queryGenerator = new QueryGenerator (settings.FaceSearchSettings, settings.IndexInfoCache.GetIndexInfo (settings.FaceIndex), clientState.Query);
            if (!(queryGenerator.ParseResult.Root is ParserEmptyNode))
               bq.AddMust (queryGenerator.GenerateQuery (FuzzyMode.None).WrapNestedQueries (ESScoreMode.max));
         }

         var resp = req.Search ();
         resp.ThrowIfError ();

         if (resp.Documents.Count>0) {
            settings.FacesAdmin.CheckStorage (resp.Documents[0].Index);
         }

         var json = new JsonMemoryBuffer ();
         json.WriteStartObject ();
         if (queryGenerator != null) {
            var transTerms = queryGenerator?.Translated;
            if (transTerms != null && transTerms.Count > 0) {
               var gTerms = new HashSet<string> ();
               foreach (var tq in transTerms) gTerms.Add (tq.ToString ());
               json.WriteProperty ("all_terms", string.Join ("\n\t", gTerms));
            }
            if (queryGenerator.ParseResult.Errors) json.WriteProperty ("query_error", true);
         }

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
         writeName (json, -1, "UNKNOWN");
         var names = faceNames.SortedNames;
         for (int i=0; i< names.Length; i++) 
            writeName (json, names[i].Id, names[i].Name);
         json.WriteEndArray ();
         json.WriteEndObject ();
         return new JsonActionResult (json);
      }

      public IActionResult Get (string storId) {
         var settings = (Settings)base.Settings;
         var storage = settings.FacesAdmin.Storage;
         if (storage == null) goto NOT_FOUND;
         var entry = storage.GetFileEntry (storId);
         if (entry == null) goto NOT_FOUND;

         byte[] buf = FileStorageAccessor.GetBytes (storage, entry);
         return new BytesActionResult ("image/jpeg", buf);

         NOT_FOUND: return new ActionResult404 ();
      }

      private const int ID_MIN = -2;
      private const int ID_CLEAR = -2;
      private const int ID_UNKNOWN = -1;

      [HttpPost]
      public IActionResult SetFace () {
         JsonObjectValue json = Request.GetBodyAsJson ();
         string id = json.ReadStr ("id");
         int faceId = json.ReadInt ("faceid");
         bool correct = json.ReadBool ("correct");

         var settings = (Settings)base.Settings;
         var names = settings.FacesAdmin.Names;
         if (faceId < ID_MIN || faceId > names.Count)
            throw new BMException ("Invalid faceId [{0}]. Face-count={1}.", faceId, names.Count);
         var c = settings.ESClient;
         var idUrl = settings.FaceIndex + "/_doc/" + Encoders.UrlDataEncode (id);
         var rec = c.SendGetJson (HttpMethod.Get, idUrl);
         rec = rec.ReadObj ("_source");
         SiteLog.Log ("Update id=" + id);
         SiteLog.Log ("-- Old record=" + rec.ToJsonString ());
         switch (faceId) {
            case ID_CLEAR: 
               rec.Remove ("names");
               rec.Remove ("explain");
               rec["src"] = "N";
               break;
            case ID_UNKNOWN: 
               rec.Remove ("names");
               rec.Remove ("explain");
               rec["src"] = correct ? "CU" : "MU";
               break;
            default: //Update
               rec.Remove ("explain");
               rec["src"] = correct ? "CK": "MK";
               var nameObj = new JsonObjectValue ();
               rec["names"] = new JsonArrayValue ((JsonValue)nameObj);
               nameObj["id"] = faceId;
               nameObj["match_score"] = 1;
               nameObj["name"] = names.NameById (faceId);
               break;
         }
         rec["updated"] = DateTime.UtcNow;
         SiteLog.Log ("-- New record=" + rec.ToJsonString ());
         c.Send (HttpMethod.Put, idUrl, HttpPayload.Create (rec)).ThrowIfError ();
         return new JsonActionResult ();
      }

      public IActionResult ClearAutoFaces () {
         var settings = (Settings)base.Settings;
         var c = settings.ESClient;
         var req = c.CreateSearchRequest(settings.FaceIndex);
         req.Query = new ESTermQuery ("src", "a");
         int cnt = 0;
         using (var recs = new ESRecordEnum (req)) {
            foreach (var d in recs) {
               var idUrl = settings.FaceIndex + "/_doc/" + Encoders.UrlDataEncode (d.Id);
               var json = d._Source;
               json.Remove ("names");
               json["src"] = "U";
               c.Send (HttpMethod.Put, idUrl, HttpPayload.Create (json)).ThrowIfError ();
               cnt++;
            }
         }
         var resp = new JsonObjectValue ("cleared", cnt);
         return new JsonActionResult (resp);
      }

   }
}

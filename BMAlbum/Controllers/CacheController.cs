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

using Bitmanager.Json;
using Bitmanager.Web;
using Microsoft.AspNetCore.Mvc;
using System.Text;

namespace BMAlbum.Controllers {
   public class CacheController : BaseController {
      public IActionResult Stats () {
         if (!isInternalOrAuthenticated ()) return new ActionResult404 ();
         var settings = (Settings)base.Settings;
         var json = new JsonMemoryBuffer ();
         var smallStats = settings.PhotoCache.GetCacheStats (CacheType.Small);
         var largeStats = settings.PhotoCache.GetCacheStats (CacheType.Large);

         json.WriteStartObject ();
         json.WritePropertyName ("small_cache");
         smallStats.WriteTo (json);
         json.WritePropertyName ("large_cache");
         largeStats.WriteTo (json);
         json.WriteEndObject ();
         return new JsonActionResult (json);
      }

      [HttpPost]
      public IActionResult Clear () {
         if (!isInternalOrAuthenticated ()) return new ActionResult404 ();
         var settings = (Settings)base.Settings;
         var json = new JsonMemoryBuffer ();
         var type = Request.ReadEnum ("type", CacheType.Both);
         type = settings.PhotoCache.Clear (type);

         json.WriteStartObject ();
         json.WriteProperty ("cleared", type.ToString ());
         json.WriteEndObject ();
         return new JsonActionResult (json);
      }

      public IActionResult RefreshStats () {
         if (!isInternalOrAuthenticated ()) return new ActionResult404 ();
         return getStats ();
      }

      private JsonActionResult getStats (RefreshParams parms=null) {
         var stats = ((Settings)base.Settings).Refresher.GetStats ();
         if (parms == null) parms = stats.Params;

         var json = new JsonObjectValue ();
         json["parms"] = stats.Params == null ? null : stats.Params.ToJson ();
         json["stats"] = stats.ToJson ();
         return new JsonActionResult (json);
      }

      [HttpPost]
      public IActionResult StartRefresh () {
         if (!isInternalOrAuthenticated ()) return new ActionResult404 ();
         var settings = (Settings)base.Settings;

         JsonObjectValue json = Request.GetBodyAsJson ();

         var parms = new RefreshParams (json);
         settings.Refresher.Trigger (parms);
         Thread.Sleep (500);
         return getStats (parms);
      }

      [HttpPost]
      public IActionResult StopRefresh () {
         if (!isInternalOrAuthenticated ()) return new ActionResult404 ();
         var settings = (Settings)base.Settings;
         settings.Refresher.StopRefresh ();
         Thread.Sleep (500);
         return getStats ();
      }

      public IActionResult Check () {
         if (!isInternalOrAuthenticated ()) return new ActionResult404 ();
         var settings = (Settings)base.Settings;
         var list = settings.Refresher.CheckNonExistingFiles (10000);
         int needed = 64;
         for (int i=0; i<list.Count; i++) needed += list[i].Length+2;
         var sb = new StringBuilder(needed);
         sb.Append (list.Count).AppendLine (" missing IDs:");
         for (int i = 0; i < list.Count; i++) sb.AppendLine (list[i]);
         return Content (sb.ToString ());
      }

      private bool isInternalOrAuthenticated() {
         return RequestCtx.IsInternalIp || isAuthenticated ();
      }
   }
}

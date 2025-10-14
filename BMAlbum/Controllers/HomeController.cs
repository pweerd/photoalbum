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

using Bitmanager.Core;
using Bitmanager.Json;
using Bitmanager.Web;
using BMAlbum.Core;
using BMAlbum.Models;
using Microsoft.AspNetCore.Mvc;

namespace BMAlbum.Controllers {
   public class HomeController : BaseController {

      public IActionResult Index () {
         var settings = (Settings)base.Settings;
         var clientState = new ClientState (RequestCtx, settings);
         if (clientState.AppMode == AppMode.Faces) {
            if (!clientState.InternalIp) return new ActionResult404 ();
         }
         if (clientState.User==null) return new ActionResult404 ();
         if (clientState.PerAlbum == TriStateBool.Unspecified && clientState.User.InitialPerAlbum != TriStateBool.Unspecified)
            clientState.PerAlbum = clientState.User.InitialPerAlbum;
         if (clientState.SortName == null && clientState.User.InitialSortMode != null)
            clientState.SetSortMode (clientState.User.InitialSortMode);

         switch (BMAlbum.User.CheckAccess(clientState.User, RequestCtx.RemoteIPClass, isAuthenticated())) {
            case _Access.NotExposed: return new ActionResult404 ();
            case _Access.MustAuthenticate: return new RequestAthenticationResult (Request);
         }

         return View (new HomeModel (this, clientState));
      }


      public IActionResult Config (string dvt) {
         var settings = (Settings)base.Settings;

         BrowserType type;
         if (!string.IsNullOrEmpty (dvt) && dvt[0] >= '0' && dvt[0] <= '9')
            type = (BrowserType)Invariant.ToInt32 (dvt);
         else
            type = Invariant.ToEnum<BrowserType> (dvt);
         
         var json = new JsonMemoryBuffer ();
         json.WriteStartObject ();

         if (RequestCtx.IsInternalIp) json.WriteProperty ("is_local", true);
         json.WriteProperty ("sortmodes_main", settings.MainSearchSettings.SortModes);
         json.WriteProperty ("sortmodes_faces", settings.FaceSearchSettings.SortModes);
         json.WritePropertyName ("lightbox_settings");
         settings.LightboxSettings.WriteClientConfig (json, type);
         json.WritePropertyName ("map_settings");
         settings.MapSettings.WriteClientConfig (json, type);
         if (settings.ExternalTracksUrl != null)
            json.WriteProperty ("external_tracks_url", settings.ExternalTracksUrl);

         json.WriteEndObject ();

         return new JsonActionResult(json);
      }


      public IActionResult Guid () {
         var g = System.Guid.NewGuid ();
         return Content (g.ToString ().Replace ("-", ""));
      }

      public IActionResult Login () {
         if (!isAuthenticated()) {
            return new RequestAthenticationResult (Request);
         }
         return Redirect ("~/");
      }

   }
}
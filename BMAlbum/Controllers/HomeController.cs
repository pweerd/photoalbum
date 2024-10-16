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
using BMAlbum.Models;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace BMAlbum.Controllers {
   public class HomeController : BaseController {

      public IActionResult Index () {
         var settings = (Settings)base.Settings;
         var clientState = new ClientState (RequestCtx, settings);
         if (Request.RouteValues.TryGetValue ("user", out var uid)) {
            clientState.User = settings.Users.GetUser (uid.ToString ());
         } else
            clientState.User = settings.Users.DefUser;

         switch (BMAlbum.User.CheckAccess(clientState.User, RequestCtx.RemoteIPClass, isAuthenticated())) {
            case _Access.NotExposed: return new ActionResult404 ();
            case _Access.MustAuthenticate: return new RequestAthenticationResult (Request);
         }

         return View (new HomeModel (this, clientState));
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
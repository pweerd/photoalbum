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

using Microsoft.AspNetCore.Mvc;
using Bitmanager.Web;
using BMAlbum.Models;


namespace BMAlbum.Controllers {
   public class FaceController : BaseController {
      public IActionResult Index () {
         var settings = (Settings)base.Settings;
         if (!RequestCtx.IsInternalIp) return new ActionResult404 ();

         var clientState = new ClientState (RequestCtx, settings);

         return View (new FaceModel (this, clientState));
      }
   }
}

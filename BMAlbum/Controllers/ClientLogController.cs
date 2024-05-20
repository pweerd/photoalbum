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
using Bitmanager.Cache;
using Bitmanager.Web;
using Microsoft.AspNetCore.Mvc;
using System.Net;
using Bitmanager.Json;
using System.Text;

namespace BMAlbum.Controllers {

   public class ClientLogController : Controller {
      static readonly Logger _clientLog = Logs.CreateLogger ("clientlog");
      static readonly LRUCache lruLogCache = new LRUCache (10, LRUCache.DisposeEntry);

      private readonly Logger clientLog;

      public ClientLogController(Logger logger=null) {
         this.clientLog = logger != null ? logger : _clientLog;
      }

      [HttpPost]
      public IActionResult Log () {
         string from = Request.HttpContext.Connection.RemoteIpAddress.ToString();
         Logger clientLog = (Logger)lruLogCache.Get (from);
         if (clientLog == null) {
            clientLog = this.clientLog.Clone (from);
            lruLogCache.Add (from, clientLog);
         }
         using (var strm = Request.Body) {
            var mem = new MemoryStream ();
            strm.CopyToAsync (mem).GetAwaiter ().GetResult ();
            mem.Position = 0;
            var json = JsonObjectValue.Load (mem);
            var msgs = json.ReadArr ("msgs", null);
            if (msgs != null) {
               for (int i=0; i<msgs.Count; i++) {
                  var obj = (JsonObjectValue)msgs[i];
                  var d = obj.ReadLong ("d", -1);
                  string time = null;
                  if (d>0) {
                     var dt = DateTimeUtils.FromUnixTimeStamp (d);
                     time = dt.ToString ("ss.fff");
                  }
                  var type = obj.ReadStr ("t", "debug");
                  var msg = obj.ReadStr ("m", "debug");
                  var lt = _LogType.ltDebug;
                  switch (type) {
                     case "error": lt = _LogType.ltError; break;
                     case "info": lt = _LogType.ltInfo; break;
                     case "warn": lt = _LogType.ltWarning; break;
                  }
                  clientLog.Log (lt, "[{0}]: {1}", time, msg);
               }
            }
         }
         return new JsonActionResult ();
      }
   }

}

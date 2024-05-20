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
using Bitmanager.Web;
using Microsoft.AspNetCore.Html;

namespace BMAlbum.Models {
   public class HomeModel {
      public readonly ClientState State;
      public readonly RequestContext RequestCtx;

      public HomeModel (BaseController c, ClientState state) {
         RequestCtx = c.RequestCtx;
         State = state;
      }

      public HtmlString GetStateAsHtmlString() {
         //var s = State.ToJsonAsHtmlString (true);
         //Logs.DebugLog.Log ("Model: state={0}", s);
         //return s;
         return State.ToJsonAsHtmlString ();
      }
   }
}

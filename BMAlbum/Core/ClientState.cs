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
using Bitmanager.Query;
using Bitmanager.Web;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.Identity;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace BMAlbum {
   public enum TriStateBool {
      Unspecified,
      True,
      False
   }

   public class ClientState : Bitmanager.Web.ClientState {
      private readonly Settings settings;
      private readonly SearchSettings SearchSettings;
      public string CacheVersion;
      public User User;
      public string Query;
      public string LastFacet;
      public string Slide;
      public readonly List<KeyValuePair<string, string>> Facets;
      public TriStateBool PerAlbum;
      public SortMode Sort;
      public bool Unhide;
      public bool InternalIp;
      public bool FaceMode;
      public int ActualPageSize;

      public ClientState (RequestContext ctx, Settings settings) : base (ctx.HttpContext.Request, settings.LightboxSettings.PageSize, false) {
         this.settings = settings;
         InternalIp = ctx.IsInternalIp;
         User = settings.Users.DefUser;
         PerAlbum = TriStateBool.True;
         Facets = new List<KeyValuePair<string, string>>();
         CacheVersion = settings.LightboxSettings.CacheVersion;

         FaceMode = ((string)ctx.HttpContext.Request.RouteValues["controller"]).StartsWith("Face");
         SearchSettings = FaceMode ? settings.FaceSearchSettings : settings.MainSearchSettings;
         Sort = SearchSettings.SortModes.Default;

         parseRequestParms (ctx.HttpContext.Request);
         ActualPageSize = (DebugFlags & Bitmanager.Web.DebugFlags.ONE) != 0 ? 1 : PageSize;
      }

      public bool ContainsFacetRequest(string what) {
         bool ret = false;
         for (int i=0; i<Facets.Count; i++) {
            if (Facets[i].Key == what) {
               ret = true;
               break;
            }
         }
         return ret;
      }

      public override StringBuilder ExportUrlParameters (StringBuilder sb) {
         base.ExportUrlParameters(sb);
         if (Unhide) optAppend (sb, "unhide");
         return sb;
      }


      protected override void parseParm (string key, string val) {
         switch (key) {
            case "u":
               User = settings.Users.GetUser (val);
               break;
            case "q":
               Query = val.TrimToNull ();
               break;
            case "per_album":
               PerAlbum = Invariant.ToEnum (val, TriStateBool.Unspecified);
               break;
            case "slide":
               Slide = val.TrimToNull ();
               break;
            case "sort":
               val = val.TrimToNull ();
               if (val != null) Sort = SearchSettings.SortModes.Find (val, true);
               break;
            case "album":
            case "year":
               LastFacet = key;
               val = val.TrimToNull ();
               for (int i=0; i<Facets.Count; i++) {
                  if (Facets[i].Key == key) {
                     Facets.RemoveAt (i);
                     break;
                  }
               }
               if (val == null || val=="Alle") {
                  if (key == "album") PerAlbum = TriStateBool.False;
               } else {
                  Facets.Add (new KeyValuePair<string, string> (key, val));
                  Sort = SearchSettings.SortModes.Default;
               }
               break;
            default:
               base.parseParm (key, val);
               break;
            case "unhide":
               if (!Invariant.TryParse (val, out Unhide)) Unhide = true;
               break;
         }
      }

      public string GetCommand() {
         var sb = new StringBuilder ().Append('&');
         optAppend (sb, "q", Query);
         switch(PerAlbum) {
            case TriStateBool.False: optAppend (sb, "per_album=false"); break;
         }

         for (int i = 0; i < Facets.Count; i++) {
            optAppend (sb, Facets[i].Key, Facets[i].Value);
         }

         if (Sort != settings.MainSearchSettings.SortModes.Default)
            sb.Append ("&sort=").Append (Sort?.Name);

         //optAppend (sb, "slide", Slide);
         return sb.Length <= 1 ? null : sb.ToString (1, sb.Length-1);
      }

      public override JsonObjectValue ToJson (JsonObjectValue container) {
         var cmd = GetCommand();
         if (cmd != null) container["cmd"] = cmd;

         //Return the state of the controls
         if (User != null && User != settings.Users.DefUser) container["u"] = User.Id;
         container["sortmodes"] = SearchSettings.SortModes.AsJsonObject ();
         container["q"] = Query ?? string.Empty;
         container["per_album"] = PerAlbum == TriStateBool.False ? false : true;
         container["sort"] = Sort?.Name;
         container["face_mode"] = FaceMode;
         if (Slide != null) container["slide"] = Slide;
         if (Facets != null && Facets.Count > 0) {
            foreach (var kvp in Facets) {
               container[kvp.Key] = kvp.Value;
            }
         }

         if (CacheVersion != null) container["cache_version"] = CacheVersion;
         if (InternalIp) container["extInfo"] = true;
         return base.ToJson (container);
      }

      public override string ToString () {
         return ToJson ().ToJsonString ();
      }
   }
}


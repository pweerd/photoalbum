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
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text;

namespace BMAlbum {
   public enum TriStateBool {
      Unspecified,
      True,
      False
   }
   public enum AppMode {
      Photos=1,
      Photo,
      Faces,
      Map
   }

   public class ClientState : Bitmanager.Web.ClientState {
      public readonly Settings Settings;
      private readonly SearchSettings SearchSettings;
      public string SortName;
      public string CacheVersion;
      public User User;
      public Pin Pin;
      public string Query;
      public string LastFacet;
      public string Slide;
      public string Album;
      public string Year;
      public string Zoom;
      public string Center;
      public readonly List<KeyValuePair<string, string>> Facets;
      public TriStateBool PerAlbum;
      public SortMode Sort;
      public AppMode AppMode;
      public bool Unhide;
      public bool InternalIp;
      public int ActualPageSize;

      public ClientState (RequestContext ctx, Settings settings) : base (ctx.HttpContext.Request, settings.LightboxSettings.PageSize, false) {
         this.Settings = settings;
         InternalIp = ctx.IsInternalIp;
         User = settings.Users.DefUser;
         PerAlbum = TriStateBool.Unspecified;
         Facets = new List<KeyValuePair<string, string>>();
         CacheVersion = settings.LightboxSettings.CacheVersion;

         var req = ctx.HttpContext.Request;
         if (req.RouteValues.TryGetValue ("user", out var uid)) {
            User = settings.Users.GetUser (uid.ToString ());
         } 
         if (User==null) User = settings.Users.DefUser;

         SortName = null;
         parseRequestParms (ctx.HttpContext.Request);
         ActualPageSize = (DebugFlags & Bitmanager.Web.DebugFlags.ONE) != 0 ? 1 : PageSize;

         SearchSettings = (AppMode== AppMode.Faces) ? settings.FaceSearchSettings : settings.MainSearchSettings;
         SetSortMode (SortName);
         if (!InternalIp) Unhide = false;
         if (AppMode==0) {
            req.RouteValues.TryGetValue ("mode", out var appModeId);
            if (appModeId != null)
               AppMode = Invariant.ToEnum<AppMode> ((string)appModeId);
            else if (((string)req.RouteValues["controller"]).StartsWith ("Face"))
               AppMode = AppMode.Faces;
            else
               AppMode = Slide != null ? AppMode.Photo : AppMode.Photos;
         }
      }

      public void SetSortMode (string sortModeName) {
         Sort = sortModeName == null ? SearchSettings.SortModes.Default : SearchSettings.SortModes.Find (sortModeName, true);
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


      protected override bool parseParm (string key, string val) {
         val = val.TrimToNull ();
         switch (key) {
            case "u":
               User = Settings.Users.GetUser (val);
               break;
            case "q":
               Query = val;
               break;
            case "per_album":
               PerAlbum = Invariant.ToEnum (val, TriStateBool.Unspecified);
               break;
            case "slide":
               Slide = val;
               break;
            case "sort":
               SortName = val;
               break;
            case "mode":
               AppMode = Invariant.ToEnum (val, AppMode);
               break;
            case "pin":
               if (val != null) {
                  Pin = Pin.Create (val);
               }
               break;
            case "center":
               Center = val; break;
            case "zoom":
               Zoom = val; break;
            case "album":
               LastFacet = key;
               removeFacet (key);
               if (val == null || val == "Alle") {
               } else {
                  Album = val;
                  Facets.Add (new KeyValuePair<string, string> (key, val));
                  //PW if (key == "album") PerAlbum = TriStateBool.False;
               }
               break;
            case "year":
               LastFacet = key;
               removeFacet (key);
               if (val == null || val=="Alle") {
               } else {
                  Year = val;
                  Facets.Add (new KeyValuePair<string, string> (key, val));
               }
               break;
            default:
               return base.parseParm (key, val);
            case "unhide":
               if (!Invariant.TryParse (val, out Unhide)) Unhide = true;
               break;
         }
         return true;
      }

      private void removeFacet (string key) {
         for (int i = 0; i < Facets.Count; i++) {
            if (Facets[i].Key == key) {
               Facets.RemoveAt (i);
               break;
            }
         }
      }

      //pw kan weg
      //public string GetCommand() {
      //   var sb = new StringBuilder ().Append('&');
      //   //pw
      //   if (Query != null) 
      //      optAppend (sb, "q", Query);
      //   else if (Slide != null)
      //      optAppend (sb, "slide", Slide);

      //   if (Pin != null) optAppend (sb, "pin", Pin.ToUrlValue());
      //   switch (PerAlbum) {
      //      case TriStateBool.False: optAppend (sb, "per_album=false"); break;
      //   }
      //   if (AppMode != AppMode.Photos) optAppend (sb, "mode=" + AppMode.ToString ().ToLowerInvariant ());

      //   for (int i = 0; i < Facets.Count; i++) {
      //      optAppend (sb, Facets[i].Key, Facets[i].Value);
      //   }

      //   if (Sort != Settings.MainSearchSettings.SortModes.Default)
      //      sb.Append ("&sort=").Append (Sort?.Name);

      //   return sb.Length <= 1 ? null : sb.ToString (1, sb.Length-1);
      //}

      public override JsonObjectValue ToJson (JsonObjectValue container) {
         //var cmd = GetCommand();
         //if (cmd != null) container["cmd"] = cmd;

         //Return the state of the controls
         if (User != null) container["user"] = User.Id;
         container["sortmodes"] = SearchSettings.SortModes.AsJsonObject ();
         if (Query != null) container["q"] = Query;
         if (Pin != null) container["pin"] = Pin.ToJson();
         container["per_album"] = PerAlbum == TriStateBool.False ? false : true;
         container["mode"] = AppMode.ToString ().ToLowerInvariant ();
         container["sort"] = Sort?.Name;
         container["lightbox_settings"] = Settings.LightboxSettings.SettingsForClient;
         container["map_settings"] = Settings.MapSettings.ToJson ();
         if (Settings.ExternalTracksUrl != null)
            container["external_tracks_url"] = Settings.ExternalTracksUrl;
         if (Slide != null) container["slide"] = Slide;
         if (Album != null) container["album"] = Album;
         if (Year != null) container["year"] = Year;
         if (Zoom != null) container["zoom"] = Zoom;
         if (Center != null) container["center"] = Center;
         if (Facets != null && Facets.Count > 0) {
            foreach (var kvp in Facets) {
               container[kvp.Key] = kvp.Value;
            }
         }

         if (CacheVersion != null) container["cache_version"] = CacheVersion;
         if (InternalIp) container["is_local"] = true;
         return base.ToJson (container);
      }

      public override string ToString () {
         return ToJson ().ToJsonString ();
      }
   }
}


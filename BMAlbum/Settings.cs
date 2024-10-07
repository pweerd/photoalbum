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
using Bitmanager.Elastic;
using Bitmanager.IO;
using Bitmanager.Query;
using Bitmanager.Storage;
using Bitmanager.Web;
using Bitmanager.Xml;
using BMAlbum.Core;
using System.Net;
using System.Reflection;
using System.Text;
using System.Xml;

namespace BMAlbum {
   public class Settings : SettingsBase {
      public readonly string MainIndex;
      public readonly string FaceIndex;
      public readonly RootReplacer Roots;
      public readonly PhotoCache PhotoCache;
      public readonly FacesAdmin FacesAdmin;
      public readonly Users Users;
      public readonly LightboxSettings LightboxSettings;
      public readonly Refresher Refresher;
      public readonly SearchSettings MainSearchSettings;
      public readonly SearchSettings FaceSearchSettings;
      public readonly ESConnection ESClient;
      public readonly ESIndexInfoCache IndexInfoCache;
      public readonly Shrinker ShrinkerSmall, ShrinkerLarge;
      public readonly MapSettings MapSettings;

      public Settings (string fn, SettingsBase oldSettings = null, string expectedVersion = null) : base (fn, oldSettings, expectedVersion) {
         SearcherCollection.SearcherFactory = SearcherFactory.Instance;

         ESClient = new ESConnection (Xml.SelectMandatoryNode ("server"));

         XmlNode indexNode = Xml.SelectMandatoryNode ("mainindex");
         MainIndex = indexNode.ReadStr ("@name", "album");
         MainSearchSettings = new SearchSettings (indexNode.SelectMandatoryNode ("search"));

         indexNode = Xml.SelectMandatoryNode ("faceindex");
         FaceIndex = indexNode.ReadStr ("@name", "album-faces");
         FaceSearchSettings = new SearchSettings (indexNode.SelectMandatoryNode ("search"));

         Roots = new RootReplacer (Xml.SelectMandatoryNode ("roots"));

         XmlNode photocacheNode = Xml.SelectMandatoryNode ("photocache");
         string cacheDir = photocacheNode.ReadPath ("@dir", @"temp\cache");
         IOUtils.ForceDirectories (cacheDir, false);
         bool cacheLarge = photocacheNode.ReadBool ("@cache_large", true);

         var ourOldSettings = (Settings)oldSettings;
         if (ourOldSettings != null && ourOldSettings.PhotoCache.CacheDir == cacheDir && ourOldSettings.PhotoCache.CacheLarge==cacheLarge) {
            PhotoCache = ourOldSettings.PhotoCache;
         } else {
            PhotoCache = new PhotoCache(cacheDir, cacheLarge);
            ourOldSettings?.PhotoCache?.Dispose ();
         }
         ShrinkerSmall = new Shrinker (photocacheNode.SelectMandatoryNode ("shrink_small"));
         ShrinkerLarge = new Shrinker (photocacheNode.SelectMandatoryNode ("shrink_large"));

         Refresher = (oldSettings != null) ? ourOldSettings.Refresher : new Refresher (this);

         LightboxSettings = new LightboxSettings (Xml.SelectSingleNode ("lightbox"));

         if (oldSettings != null) {
            IndexInfoCache = ((Settings)oldSettings).IndexInfoCache;
         }
         IndexInfoCache ??= new ESIndexInfoCache (ESClient, ComputeID);

         FacesAdmin = new FacesAdmin (Xml.SelectMandatoryNode ("faces_admin"), SiteLog);

         Users = new Users (Xml.SelectSingleNode ("users"), doesUserExist);
         Users.Dump (WebGlobals.Instance.SiteLog);

         MapSettings = new MapSettings (Xml.SelectSingleNode ("map"));

         WebGlobals.Instance.SiteLog.Log("Lightbox client settings:\n{0}", LightboxSettings.SettingsForClient);
      }

      public bool doesUserExist(User user) {
         var req = ESClient.CreateSearchRequest (MainIndex);
         req.Query = user.Filter;
         var resp = req.Count ();
         return resp.IsOK() && resp.Count > 0;
      }

      private static void Instance_OnStop (object? sender, EventArgs e) {
         var g = (WebGlobals)sender;
         g.SiteLog.Log ("Disposing image-cache");
         var settings = (Settings)g.Settings;
         if (settings != null) {
            settings.Refresher?.StopThread ();
            settings.PhotoCache?.Dispose ();
            settings.FacesAdmin?.Dispose (); 
         }
      }
      static Settings () {
         WebGlobals.Instance.OnStop += Instance_OnStop;
      }
   }
}

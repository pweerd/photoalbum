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
using Bitmanager.IO;
using Bitmanager.Json;
using Bitmanager.Web;
using BMAlbum.Core;
using BMAlbum.Models;
using Microsoft.AspNetCore.Mvc;
using System.Data;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace BMAlbum.Controllers {
   public class UploadController : BaseController {
      private static bool zipCreated;

      public IActionResult Index () {
         var settings = (Settings)base.Settings;
         var uploadSettings = settings.UploadSettings;
         if (uploadSettings == null || !uploadSettings.Active) return NotFound ();

         var clientState = new ClientState (RequestCtx, settings);
         clientState.StateProperties.Add ("upload_text", uploadSettings.UploadText);
         clientState.StateProperties.Add ("remove_text", uploadSettings.RemoveText);
         clientState.StateProperties.Add ("filter", uploadSettings.Filter);
         clientState.StateProperties.Add ("max_file_size", uploadSettings.MaxMBPerFile);
         return View (new UploadModel (this, clientState));
      }
      public IActionResult Admin () {
         var settings = (Settings)base.Settings;

         var clientState = new ClientState (RequestCtx, settings);
         return View (new UploadModel (this, clientState));
      }

      [HttpPost]
      public IActionResult AdminExec (IFormCollection formdata) {
         var settings = (Settings)base.Settings;
         var uploadSettings = settings.UploadSettings;
         var clientState = new ClientState (RequestCtx, settings);
         if (!RequestCtx.IsInternalIp && !Request.IsAuthenticated()) goto NOT_FOUND;

         var referrer = Request.Headers.Referer.ToString();
         if (string.IsNullOrEmpty (referrer)) goto NOT_FOUND;

         string cmd=null;
         if (HttpContext.Request.Form.TryGetValue ("command", out var v)) {
            cmd = v.ToString ().TrimToNull ();
         }
         switch (cmd) {
            case "clear": break;
            case "activate":
               uploadSettings.Active = true;
               break;
            case "deactivate":
               uploadSettings.Active = false;
               break;
            default: throw new BMException ("Unknown admin action: [{0}].", cmd); 
         }
         SiteLog.Log ("Executed cmd={0}.", cmd);

         return Redirect (referrer);

      NOT_FOUND:
         return NotFound ();
      }

      private static readonly Regex invalidChars = new Regex(@"[<>\:\|]", RegexOptions.Compiled | RegexOptions.CultureInvariant);

      public async Task<IActionResult> Post (IFormCollection formdata) {
         var settings = (Settings)base.Settings;
         var uploadSettings = settings.UploadSettings;
         if (uploadSettings == null || !uploadSettings.Active) return NotFound ();

         string user=null;
         if (HttpContext.Request.Form.TryGetValue("user", out var v)) {
            user = v.ToString ().TrimToNull();
         }

         try {
            if (user == null)
               throw new UserError ("Cannot upload without a user name.");

            if (invalidChars.IsMatch (user)) throw new UserError ("Invalid characters in user's name.");
            var root = Path.Combine (WebGlobals.Instance.AppRoot, uploadSettings.UploadRoot, user);

            var files = HttpContext.Request.Form.Files;
            var MBs = getUsersExistingMB(root, out var numFiles);
            if (uploadSettings.MaxFiles >= 0 && numFiles > uploadSettings.MaxFiles)
               throw new UserError ("Quotum exceeded: max allowed files={0}. Existing files={1}", uploadSettings.MaxFiles, numFiles);
            if (uploadSettings.MaxMB >= 0 && MBs > uploadSettings.MaxMB)
               throw new UserError ("Quotum exceeded: storage limit={0}MB. Already stored: {1}MB", uploadSettings.MaxMB, MBs);
            IOUtils.ForceDirectories (root, false);

            foreach (var file in files) {
               if (file.Length <= 0) continue;
               using (var fileStream = new FileStream (Path.Combine (root, file.FileName), FileMode.Create)) {
                  await file.CopyToAsync (fileStream);
               }
            }
            zipCreated = false;
         } catch (UserError err) {
            return new JsonActionResult (new JsonObjectValue("error", err.Message));
         }
         return new JsonActionResult ();
      }


      public IActionResult Download (IFormCollection formdata) {
         var g = WebGlobals.Instance;
         var settings = (Settings)base.Settings;
         var uploadSettings = settings.UploadSettings;
         if (uploadSettings == null || !uploadSettings.Active) return NotFound ();

         var root = Path.Combine (g.AppRoot, uploadSettings.UploadRoot);


         var zipRootName = g.TempDir.GetTempFileName("upload");
         var zipGens = new FileGenerations2  (zipRootName, ".zip", 2);
         var zipFile = zipGens.Target;
         if (zipFile == null || !zipCreated) {
            zipFile = zipGens.CreateTargetName();
            if (!System.IO.File.Exists (zipFile) || !zipCreated) {
               ZipFile.CreateFromDirectory (root, zipFile, CompressionLevel.NoCompression, true);
            }
            zipGens.CreateOrUpdateLink ();
         }
         return PhysicalFile (zipFile, MimeType.Zip, true);
      }


      private int getUsersExistingMB (string uploadRoot, out int fileCount) {
         fileCount = 0;
         if (!Directory.Exists (uploadRoot)) return 0;

         long bytes = 0;
         int files = 0;
         var dirInfo = new DirectoryInfo(uploadRoot);
         foreach (var fi in dirInfo.EnumerateFileSystemInfos ("*", SearchOption.TopDirectoryOnly)) {
            if ((fi.Attributes & FileAttributes.Directory) != 0) continue;
            ++files;
            bytes += ((FileInfo)fi).Length;
         }
         fileCount = files;
         return (int)Math.Round (bytes / (1024.0 * 1024.0));
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

         return new JsonActionResult (json);
      }


      class UserError : Exception {
         public UserError (string err) : base (err) { }
         public UserError (string fmt, params object[] args) : base (Invariant.Format(fmt, args)) { }
      }

   }
}

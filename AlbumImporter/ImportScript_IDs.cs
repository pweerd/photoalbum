/*
 * Copyright © 2023, De Bitmanager
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
using Bitmanager.Xml;
using Bitmanager.ImportPipeline;
using System.Xml;
using IStreamProvider = Bitmanager.ImportPipeline.StreamProviders.IStreamProvider;
using Bitmanager.Json;
using System.Text.RegularExpressions;

namespace AlbumImporter {
   public class ImportScript_Ids {
      private readonly HashSet<string> skippedExts, okExts;
      private XmlNode prev;
      private string user;
      private string root;
      private Regex defMimeFilter, mimeFilter;
      public ImportScript_Ids () {
         skippedExts = new HashSet<string> ();
         okExts = new HashSet<string> ();
      }

      public object OnId (PipelineContext ctx, object value) {
         var elt = (IStreamProvider)value;
         var ext = Path.GetExtension (elt.FullName).ToLowerInvariant ();
         var mime = MimeType.GetMimeTypeFromExt (ext);

         if (elt.ContextNode != prev) {
            prev = elt.ContextNode;
            user = prev.ReadStr ("@user");
            root = elt.VirtualRoot;
            var tmp = prev.ReadStrRaw ("@mime_filter", _XmlRawMode.Trim | _XmlRawMode.DefaultOnNull, null);
            if (tmp == null) mimeFilter = defMimeFilter;
            else if (tmp.Length == 0) mimeFilter = null;
            else mimeFilter = new Regex (tmp, RegexOptions.Compiled | RegexOptions.CultureInvariant);
         }

         if (mime == null || (mimeFilter != null && !mimeFilter.IsMatch(mime))) {
            skippedExts.Add (Path.GetExtension (elt.FullName).ToLowerInvariant ());
            ctx.ActionFlags |= _ActionFlags.Skip;
            return value;
         }
         
         if (elt.Size < 10) {
            string msg = Invariant.Format ("File is not an image/video or is damaged.\nFile={0}", elt.FullName);
            ctx.ImportLog.Log (_LogType.ltError, msg);
            Logs.ErrorLog.Log (_LogType.ltError, msg);
            ctx.ActionFlags |= _ActionFlags.Skip;
            return value;
         }

         okExts.Add (ext);
         var ep = (JsonObjectValue)ctx.Action.Endpoint.GetField (null);
         ep["id"] = elt.VirtualName;
         ep["root"] = root;
         ep["user"] = user;
         ep["date"] = elt.LastModifiedUtc;
         return value;
      }

      public object OnDatasourceEnd (PipelineContext ctx, object value) {
         ctx.ImportLog.Log ("Following exts were skipped (not matching mime_filter):\n" + string.Join ("\n", skippedExts));
         ctx.ImportLog.Log ("Following exts were OK:\n" + string.Join ("\n", okExts));
         return value;
      }

      public object OnDatasourceStart (PipelineContext ctx, object value) {
         var tmp = ctx.DatasourceAdmin.ContextNode.ReadStrRaw ("@mime_filter", _XmlRawMode.Trim | _XmlRawMode.DefaultOnNull, "^image|^video");
         defMimeFilter = tmp.Length == 0 ? null : new Regex (tmp, RegexOptions.Compiled | RegexOptions.CultureInvariant);

         ctx.ImportLog.Log ("Default mime_filter: {0}", defMimeFilter);
         return value;
      }
   }
}

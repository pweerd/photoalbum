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
using Bitmanager.Imaging;
using Bitmanager.ImportPipeline.StreamProviders;
using Bitmanager.ImportPipeline;
using Bitmanager.IO;
using Bitmanager.Json;
using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using Bitmanager.Elastic;
using Bitmanager.Xml;
using Bitmanager.Storage;
using SixLabors.ImageSharp.PixelFormats;

namespace AlbumImporter {
   public class ImportScript_Captions : ImportScriptBase {
      private readonly JsonClient client = new JsonClient (new Uri ("http://127.0.0.1:5000/"));
      private CaptionCollection existingCaptions;
      private bool sameIndex; //PW Nakijken
      private int maxCount;

      public ImportScript_Captions () {
      }

      public object OnDatasourceStart (PipelineContext ctx, object value) {
         Init (ctx, true);

         string url = base.copyFromUrl;
         if (url == null && !fullImport) url = base.oldIndexUrl;
         existingCaptions = new CaptionCollection (ctx.ImportLog, url);

         if (!fullImport) existingCaptions.Load (ctx.ImportLog, ctx.Action.Endpoint);
         ctx.ImportLog.Log (_LogType.ltTimerStart, "captions: starting Captions service");
         ctx.ImportEngine.ProcessHostCollection.EnsureStarted ("caption");
         ctx.ImportLog.Log (_LogType.ltTimerStop, "captions: started");

         ctx.ImportLog.Log ("Starting captions import. FullImport={0}, copy_from={1}, existing records={2}",
            fullImport,
            copyFromUrl,
            existingCaptions.Count);

         handleExceptions = true;
         return null;
      }

      public object OnId (PipelineContext ctx, object value) {
         idInfo = (IdInfo)value;
         string id = idInfo.Id;
         var dst = ctx.Action.Endpoint.Record;
         if (existingCaptions.TryGetValue (idInfo.Id, out var caption)) {
            if (sameIndex) {
               ctx.ActionFlags |= _ActionFlags.Skip;
            } else {
               dst["_id"] = id;
               dst["ts"] = caption.Ts;
               dst["text_en"] = caption.Caption_EN;
               dst["text_nl"] = caption.Caption_NL;
            }
            // ctx.ImportLog.Log ("Id={0}, existing", id);
            return value;
         }

         ctx.ImportLog.Log ("Processing Id={0}", id);
         string fn = idInfo.FileName;

         //Fetch caption from the caption-server
         var resp = client.Get ("caption?file=" + Encoders.UrlDataEncode (getItemFileName()), true);

         dst["_id"] = id;
         dst["ts"] = DateTime.UtcNow;
         dst["text_en"] = getCaption (resp.Value, "captions_en");
         dst["text_nl"] = getCaption (resp.Value, "captions_nl");

         WaitAfterExtract ();
         return null;
      }


      private JsonValue getCaption (JsonObjectValue obj, string key) {
         JsonArrayValue arr = obj.ReadArr (key);
         switch (arr.Count) {
            case 1: return arr[0];

            case 0: 
               logger.Log (_LogType.ltError, "No caption for file={0}", idInfo.FileName);
               return null;
            
            default:
               logger.Log (_LogType.ltError, "Unexpected count for file={0}: {1}", idInfo.FileName, arr.Count);
               if (arr.Count > maxCount) maxCount = arr.Count;
               return arr[0];
         }
      }

   }
}

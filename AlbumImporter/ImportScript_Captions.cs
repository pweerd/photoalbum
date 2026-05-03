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

using AlbumImporter.Captions;
using Bitmanager.Core;
using Bitmanager.Http;
using Bitmanager.ImportPipeline;
using Bitmanager.Json;

namespace AlbumImporter {
   public class ImportScript_Captions : ImportScriptBase {
      private readonly HttpSession http;
      private CaptionCollection existingCaptions;
      private GoogleTranslator translator;

      private int maxCount;

      public ImportScript_Captions () {
         http = new HttpSession ();
      }

      public object OnDatasourceStart (PipelineContext ctx, object value) {
         Init (ctx, true);

         translator = new GoogleTranslator (ctx.ImportEngine.CancelToken);
         existingCaptions = new CaptionCollection();
         if (!fullImport || !forceRebuild) {
            if (curIndex != null) {
               existingCaptions.Load(ctx.ImportLog, activeOldIndex, !sameIndexOrNotExist);
            }
         }

         ctx.ImportLog.Log (_LogType.ltTimerStart, "captions: starting Captions service");
         ctx.ImportEngine.ProcessHostCollection.EnsureStarted ("caption");
         ctx.ImportLog.Log (_LogType.ltTimerStop, "captions: started");

         ctx.ImportLog.Log("Starting captions(python) import. Flags={0}, existing records={1}, cur={2}, old={3}, copy={4}",
            ctx.ImportFlags,
            existingCaptions.Count,
            curIndex,
            oldIndex,
            copyFromIndex);

         handleExceptions = true;
         return null;
      }

      public object OnId (PipelineContext ctx, object value) {
         idInfo = (IdInfo)value;
         string id = idInfo.Id;
         var dst = ctx.Action.Endpoint.Record;
         if (existingCaptions.TryGetValue (idInfo.Id, out var captionRec)) {
            if (captionRec.Failed && forceRebuild) goto PROCESS;
            if (captionRec.CopyNeeded) {
               captionRec.Export (dst);
            } else {
               ctx.ActionFlags |= _ActionFlags.Skip;
            }
            return value;
         }

      PROCESS:
         ctx.ImportLog.Log ("Processing Id={0}", id);
         string fn = idInfo.FileName;

         //Fetch caption from the caption-server
         var resp = http.Get ("http://127.0.0.1:5000/caption?file=" + Encoders.UrlDataEncode (getItemFileName()), CancellationToken.None);
         resp.ThrowIfError ();

         dst["_id"] = id;
         dst["ts"] = DateTime.UtcNow;
         string caption = null;
         try {
            caption = getCaption (resp.Json, "captions_en");
            dst["text_en"] = caption;
         } catch (Exception ex) {
            dst["failed"] = true;
            OnError (ctx, ex);
            return null; //Just add the record
         }
         dst["text_nl"] = translator.Translate (caption, "nl", "en");

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

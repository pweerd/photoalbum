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
using Bitmanager.ImportPipeline;
using Bitmanager.IO;
using Bitmanager.Json;
using Bitmanager.Http;
using Bitmanager.Xml;

namespace AlbumImporter {
   public class ImportScript_Captions_Ollama : ImportScriptBase {
      private OllamaClient ollamaClient;
      private GoogleTranslator translator;
      private CaptionCollection existingCaptions;
      private string prompt;
      private int temperature;

      public ImportScript_Captions_Ollama () {
      }

      public object OnDatasourceStart(PipelineContext ctx, object value) {
         //Initialize OllamaClient
         //The complete request or the prompt/temperature are customizable from the xml.
         temperature = -1;
         JsonObjectValue llmRequest = null;
         JsonObjectValue options = null;
         var llmNode = ctx.DatasourceAdmin.ContextNode.SelectSingleNode("llm_request");
         if (llmNode != null) {
            string txt = llmNode.InnerText.TrimToNull();
            if (txt != null) llmRequest = JsonObjectValue.Parse(txt);
            int temp = llmNode.ReadInt("@temperature", -1);
            if (temp != -1) {
               if (llmRequest == null) llmRequest = OllamaClient.CreateDefaultTemplate();
               options = llmRequest.ReadObj("options", null);
               if (options == null) llmRequest["options"] = new JsonObjectValue();
               options["temperature"] = temp;
            }
            txt = llmNode.ReadStr("@prompt", null);
            if (txt != null) {
               if (llmRequest == null) llmRequest = OllamaClient.CreateDefaultTemplate();
               llmRequest["prompt"] = txt;
            }
         }
         ollamaClient = new OllamaClient(OllamaClient.DEF_URL, llmRequest, ctx.ImportEngine.CancelToken);
         prompt = ollamaClient.Template.ReadStr("prompt", null);
         options = ollamaClient.Template.ReadObj("options", null);
         if (options != null)
            temperature = options.ReadInt("temperature", -1);
         ctx.ImportLog.Log("Ollama client initialized with prompt [{0}] and temperature [{1}].", prompt, temperature);

         //Init translator
         translator = new GoogleTranslator(ctx.ImportEngine.CancelToken);

         //Init rest
         Init(ctx, true);

         string url = base.copyFromUrl;
         if (url == null && !fullImport) url = base.oldIndexUrl;
         existingCaptions = new CaptionCollection(ctx.ImportLog, url);

         if (!fullImport) existingCaptions.Load(ctx.ImportLog, ctx.Action.Endpoint);

         ctx.ImportLog.Log("Starting captions import. FullImport={0}, copy_from={1}, existing records={2}, sameIndex={3}",
            fullImport,
            copyFromUrl,
            existingCaptions.Count,
            sameIndex);

         handleExceptions = true;
         return null;
      }

      public object OnId (PipelineContext ctx, object value) {
         idInfo = (IdInfo)value;
         string id = idInfo.Id;
         var dst = ctx.Action.Endpoint.Record;
         if (existingCaptions.TryGetValue (idInfo.Id, out var captionRec)) {
            if (sameIndex) {
               ctx.ActionFlags |= _ActionFlags.Skip;
            } else {
               dst["_id"] = id;
               dst["ts"] = captionRec.Ts;
               dst["text_en"] = captionRec.Caption_EN;
               dst["text_nl"] = captionRec.Caption_NL;
               if (captionRec.Prompt != null) dst["prompt"] = captionRec.Prompt;
               if (captionRec.Temperature != -1) dst["temperature"] = captionRec.Temperature;
            }
            // ctx.ImportLog.Log ("Id={0}, existing", id);
            return value;
         }

         if (idInfo.MimeType != MimeType.Jpeg && idInfo.MimeType != MimeType.Png) {
            ctx.ImportLog.Log(_LogType.ltInfo, "Ignored, not a jpg/png: Id={0}", id);
            ctx.ActionFlags |= _ActionFlags.Skip;
            return value;
         }

         ctx.ImportLog.Log ("Processing Id={0}", id);
         string fn = idInfo.FileName;

         //Fetch caption from the caption-server
         string caption = ollamaClient.PostGetResponse(idInfo.FileName);

         dst["_id"] = id;
         dst["ts"] = DateTime.UtcNow;
         dst["text_en"] = caption;
         dst["text_nl"] = translator.Translate(caption, "nl", "en");
         dst["prompt"] = prompt;
         dst["temperature"] = temperature;

         WaitAfterExtract();
         return null;
      }
   }
}

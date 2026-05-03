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
using System.Xml;
using Bitmanager.Xml;
using Bitmanager.Ocr;
using AlbumImporter.Ocr;

namespace AlbumImporter {
   /// <summary>
   /// Importscript that is generating the OCR-data from photo's
   /// </summary>
   public class ImportScript_Ocr: ImportScriptBase {
      private OcrEngineCache engineCache;
      private OcrCollection existingOcr;
      private HashSet<string> idsToSkip;

      public object OnDatasourceStart (PipelineContext ctx, object value) {
         base.Init (ctx, true);

         OcrConfig config;
         XmlNode ocrNode = ctx.DatasourceAdmin.ContextNode.SelectMandatoryNode ("ocr");
         config = new OcrConfig (ocrNode);
         config.Check ();
         engineCache = new OcrEngineCache (config);
         engineCache.Acquire ().Dispose (); //Try to create engine, just to make sure that we early fail.

         idsToSkip = loadToSkip (ctx.DatasourceAdmin.ContextNode.SelectSingleNode ("skip"));
         ctx.ImportLog.Log ("List of id's to be skipped contains {0} ID's", idsToSkip?.Count);

         existingOcr = new OcrCollection();
         if (!fullImport || !forceRebuild) {
            if (curIndex != null) {
               existingOcr.Load(ctx.ImportLog, activeOldIndex, !sameIndexOrNotExist, true);
            }
         }

         ctx.ImportLog.Log("Starting OCR import. Flags={0}, existing records={1}, cur={2}, old={3}, copy={4}",
            ctx.ImportFlags,
            existingOcr.Count,
            curIndex,
            oldIndex,
            copyFromIndex);

         handleExceptions = true;
         return null;
      }

      private static HashSet<string> loadToSkip(XmlNode node) {
         HashSet<string> ret = null;
         if (node != null) {
            foreach (var line in node.InnerText.Split('\n')) {
               var trimmed = line.Trim ();
               if (trimmed.Length == 0) continue;
               if (ret == null) ret = new HashSet<string> ();
               ret.Add (trimmed);
            }
         }
         return ret;
      }
      public object OnDatasourceEnd (PipelineContext ctx, object value) {
         handleExceptions = false;
         ctx.ImportLog.Log ("Disposing OCR engine cache");
         engineCache?.Dispose (); 
         return null;
      }

      public object OnId (PipelineContext ctx, object value) {
         idInfo = (IdInfo)value;
         if (idsToSkip != null && idsToSkip.Contains (idInfo.Id)) {
            ctx.ActionFlags |= _ActionFlags.Skip;
            return value;
         }
         var dst = ctx.Action.Endpoint.Record;
         if (existingOcr.TryGetValue (idInfo.Id, out var ocr)) {
            if (ocr.CopyNeeded) {
               dst["_id"] = idInfo.Id;
               dst["ts"] = ocr.Ts;
               if (ocr.Text != null) {
                  dst["text"] = ocr.Text;
                  dst["text_len"] = ocr.Text.Length;
               }
            } else {
               ctx.ActionFlags |= _ActionFlags.Skip;
            }
            return value;
         }

         ctx.ImportLog.Log ("OCR Id={0}", idInfo.Id);
         dst["_id"] = idInfo.Id;
         dst["ts"] = DateTime.UtcNow;

         OcrEngine ocrEngine = null;
         Pix pix = Pix.Load (getItemFileName());
         try {
            if (!pix.IsGrayScale ()) Pix.Assign (ref pix, pix.ConvertRGBToGray ());
            ocrEngine = engineCache.Acquire ();
            var result = ocrEngine.DoOcr (pix, OcrInfoLevel.OnlyWords);
            string txt = result.Text.TrimToNull ();
            if (txt != null) {
               dst["text"] = txt;
               dst["text_len"] = txt.Length;
            }
         } finally {
            ocrEngine?.Dispose ();
            pix.Dispose ();
         }
         WaitAfterExtract ();
         return null;
      }

   }
}

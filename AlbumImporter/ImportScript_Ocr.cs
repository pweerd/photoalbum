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
using System.Xml;
using Bitmanager.Xml;
using Bitmanager.Ocr;
using System.Collections;
using static System.Net.Mime.MediaTypeNames;

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

         string url = base.copyFromUrl;
         if (url == null && !fullImport) url = base.oldIndexUrl;
         existingOcr = new OcrCollection (ctx.ImportLog, url, true);
         ctx.ImportLog.Log ("OCR import FINGERPRINT: [{0}]", existingOcr.FingerPrint);

         ctx.ImportLog.Log ("Starting OCR import. FullImport={0}, copy_from={1}, existing records={2}", 
            fullImport, 
            copyFromUrl, 
            existingOcr.Count);

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

      //fullImport without copy_from => coll is empty
      //fullimport with copy_from    => emit existing or generated
      //incrImport without copy_from => skip if found
      //incrImport with copy_from    => impossible
      public object OnId (PipelineContext ctx, object value) {
         idInfo = (IdInfo)value;
         if (idsToSkip != null && idsToSkip.Contains (idInfo.Id)) {
            ctx.ActionFlags |= _ActionFlags.Skip;
            return value;
         }
         var dst = ctx.Action.Endpoint.Record;
         if (existingOcr.TryGetValue (idInfo.Id, out var ocr)) {
            if (fullImport) {
               dst["_id"] = idInfo.Id;
               dst["ts"] = ocr.Ts;
               if (ocr.Text != null) dst["text"] = ocr.Text;
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
            if (txt != null) dst["text"] = txt;
         } finally {
            ocrEngine?.Dispose ();
            pix.Dispose ();
         }
         WaitAfterExtract ();
         return null;
      }

   }
}

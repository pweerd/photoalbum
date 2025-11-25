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

namespace AlbumImporter {
   public class ImportScript_ImageHash : ImportScriptBase {
      private PHashCollection existingFingerprints;
      private bool sameIndex; //PW Nakijken
      private int maxCount;

      public ImportScript_ImageHash() {
      }

      public object OnDatasourceStart (PipelineContext ctx, object value) {
         Init (ctx, true);

         string url = base.copyFromUrl;
         if (url == null && !fullImport) url = base.oldIndexUrl;
         existingFingerprints = new PHashCollection (ctx.ImportLog, url);

         if (!fullImport) existingFingerprints.Load (ctx.ImportLog, ctx.Action.Endpoint);

         ctx.ImportLog.Log ("Starting fingerprints import. FullImport={0}, copy_from={1}, existing records={2}",
            fullImport,
            copyFromUrl,
            existingFingerprints.Count);

         handleExceptions = true;
         return null;
      }

      public object OnId (PipelineContext ctx, object value) {
         idInfo = (IdInfo)value;
         string fn = idInfo.FileName;
         if (idInfo.MimeType == null || !idInfo.MimeType.StartsWith("image/")) {
            ctx.ActionFlags |= _ActionFlags.Skip;
            goto EXIT_RTN;
         }
         string id = idInfo.Id;
         var dst = ctx.Action.Endpoint.Record;
         PHashItemItem fp;
         if (existingFingerprints.TryGetValue (idInfo.Id, out fp)) {
            if (sameIndex) {
               ctx.ActionFlags |= _ActionFlags.Skip;
            } else {
               fp.Save (dst);
            }
            // ctx.ImportLog.Log ("Id={0}, existing", id);
            return value;
         }

         ctx.ImportLog.Log ("Processing Id={0}", id);

         dst["_id"] = id;
         dst["ph1"] = PHash.GetFingerprint(idInfo.FileName).ToString("X");
         dst["ph2"] = PHash.GetFingerprint3(idInfo.FileName).ToString("X");
         dst["ts"] = DateTime.UtcNow;

         WaitAfterExtract();

      EXIT_RTN:
         return null;
      }
   }
}

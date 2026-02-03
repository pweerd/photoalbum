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

using Bitmanager.ImportPipeline;
using AlbumImporter.PHash;

namespace AlbumImporter {
   public class ImportScript_ImageHash : ImportScriptBase {
      private PHashCollection existingHashes;

      public ImportScript_ImageHash() {
      }

      public object OnDatasourceStart(PipelineContext ctx, object value) {
         Init(ctx, true);

         existingHashes = new PHashCollection();
         if (!fullImport || !forceRebuild) {
            if (curIndex != null) {
               existingHashes.Load(ctx.ImportLog, activeOldIndex, !sameIndex);
            }
         }


         ctx.ImportLog.Log("Starting perceptual hashes import. Flags={0}, existing records={1}, cur={2}, old={3}, copy={4}",
            ctx.ImportFlags,
            existingHashes.Count,
            curIndex,
            oldIndex,
            copyFromIndex);

         handleExceptions = true;
         return null;
      }

      public object OnId(PipelineContext ctx, object value) {
         idInfo = (IdInfo)value;
         string fn = idInfo.FileName;
         if (idInfo.MimeType == null || !idInfo.MimeType.StartsWith("image/")) {
            ctx.ActionFlags |= _ActionFlags.Skip;
            goto EXIT_RTN;
         }
         string id = idInfo.Id;
         var dst = ctx.Action.Endpoint.Record;
         PHashItemItem hash;
         if (existingHashes.TryGetValue(idInfo.Id, out hash)) {
            if (hash.CopyNeeded) {
               hash.Save(dst);
            }
            else {
               ctx.ActionFlags |= _ActionFlags.Skip;
            }
            return value;
         }

         ctx.ImportLog.Log("Processing Id={0}", id);

         dst["_id"] = id;
         dst["ph1"] = PHash.PHash.GetFingerprint (idInfo.FileName).ToString ("X");
         dst["ph2"] = PHash.PHash.GetFingerprint3(idInfo.FileName).ToString("X");
         dst["ts"] = DateTime.UtcNow;

         WaitAfterExtract();

      EXIT_RTN:
         return null;
      }
   }
}

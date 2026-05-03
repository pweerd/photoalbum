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

using AlbumImporter.FaceRecognition;
using Bitmanager.AlbumTools;
using Bitmanager.Core;
using Bitmanager.Elastic;
using Bitmanager.ImportPipeline;
using Bitmanager.Json;
using Bitmanager.Xml;
using MetadataExtractor;
using System.Security.Cryptography;

namespace AlbumImporter {

   public class ImportScript_FaceMatcher : ImportScriptBase {
      private List<DbFace> existingFaces;
      private FaceNames faceNames;
      private Storages storages;
      private float[] weightPerFaceCount;
      private FaceStatistics faceStats;
      private TargetFaces targetFaces;


      private IFaceScorer faceScorer;
      private float threshold;

      public object OnDatasourceStart (PipelineContext ctx, object value) {
         Init (ctx, true);

         //Load the facecount weights
         var weightNode = ctx.DatasourceAdmin.ContextNode.SelectMandatoryNode ("weight");
         this.threshold = (float)weightNode.ReadFloat ("@threshold");
         var min = (float)weightNode.ReadFloat ("@min_weight");
         var factors = new List<float> (20);
         factors.Add (0);
         factors.Add (1f);
         var logBase = Math.Log2(weightNode.ReadFloat ("@face_factor"));
         for (int i = 1; i < 100000; i++) {
            float f = (float)Math.Pow (2, logBase * i);
            if (f < min) break;
            factors.Add (f);
         }
         factors.Add (min);
         weightPerFaceCount = factors.ToArray ();
         ctx.ImportLog.Log ("Dumping face-count weight. Threshold={0:F2}", threshold);
         for (int i = 1; i < weightPerFaceCount.Length; i++) {
            ctx.ImportLog.Log ("-- face-count [{0}]: {1}", i, weightPerFaceCount[i]);
         }
         faceScorer = new FaceScorer (weightPerFaceCount);


         //Read all face names and faces
         faceNames = ReadFaceNames ();
         var coll = new FaceCollection ();
         coll.Load (
            ctx.ImportLog,
            activeOldIndex,
            !sameIndexOrNotExist
         );
         existingFaces = coll.GetFaces ();

         //save old face-stats if we process in-place
         faceStats = new FaceStatistics (logger, faceNames, ctx.ImportEngine.Xml.BaseDir);
         if (sameIndexOrNotExist) 
            faceStats.LoadExisting (activeOldIndex, true);


         logger.Log ("Loading storages");
         storages = fullImport
            ? new Storages (faceAdminDir, curIndex.Timestamp, activeOldIndex.Timestamp)
            : new Storages (faceAdminDir, curIndex.Timestamp);


         logger.Log ("Loading embeddings from storage");
         foreach (var f in existingFaces) assignEmbedding (f);

         logger.Log ("Loading target faces");
         targetFaces = loadTargetFaces (ctx, existingFaces);

         ctx.ImportLog.Log ("Starting face matching. FullImport={0}, target faces={1}",
            fullImport,
            targetFaces.Count);

         return null;
      }

      public object OnDatasourceEnd (PipelineContext ctx, object value) {

         //In case of a different index: emit all manual/corrected faces that were not added before
         var ep = (ESDataEndpoint)ctx.Action.Endpoint;
         if (!sameIndexOrNotExist) {
            int exported=0;
            foreach (var f in existingFaces) {
               if (!f.CopyNeeded) continue;
               if (!f.NameSrc.IsManualDefined ()) continue;

               if (f.FaceStorageId >= 0) {
                  var key = f.FaceStorageId.ToString();
                  storages.CopyOldToCur (key, key);
               }
               f.Export (ep.Record);
               ctx.Pipeline.HandleValue (ctx, "record/face", f);
               exported++;
            }
            ctx.ImportLog.Log (_LogType.ltInfo, "DatasourceEnd: exported {0} manual/corrected faces that had not hit from the ID datasource.", exported);
         }
         ep.FlushCache ();
         curIndex.Refresh ();


         ctx.ImportLog.Log ("Closing storage file(s)");
         storages?.Dispose ();

         try {
            var syncher = new StorageSyncher (ctx.ImportLog, curIndex);
            syncher.Synchronize (faceAdminDir);
         } catch (Exception e) {
            ctx.ImportLog.Log (e, "Failed to synchronize: {0}", e.Message);
         }

         if (sameIndexOrNotExist) {
            faceStats.DumpNameUsage (curIndex);
            faceStats.DumpDifferences (curIndex);
         } else {
            faceStats.DumpNameUsage (curIndex, activeOldIndex, true);
            faceStats.DumpDifferences (curIndex, activeOldIndex, true);
         }
         DumpDuplicateNames (logger, curIndex);
         return null;
      }


      public object OnId (PipelineContext ctx, object value) {
         idInfo = (IdInfo)value;
         var facesInPhoto = FaceCollection.GetExistingFacesForId (existingFaces, idInfo.Id);
         if (facesInPhoto == null) {
            goto EXIT_RTN; //No face-record found
         }


         var ep = ctx.Action.Endpoint;
         if (facesInPhoto[0].FaceCount == 0) {//No faces in this photo
            if (facesInPhoto[0].CopyNeeded) {
               facesInPhoto[0].Export (ep.Record);
               ctx.Pipeline.HandleValue (ctx, "record/face", ep.Record);
            }
            goto EXIT_RTN;
         }

         ctx.Emitted += facesInPhoto.Count - 1; //correct for the #faces (+1 was done by the datasource)
         
         //Assign matches
         for (int i = 0; i < facesInPhoto.Count; i++) {
            var f = facesInPhoto[i];
            if ((f.NameSrc & NameSource.ManualOrCorrected) != 0) continue;
            matchFace (f, targetFaces);
         }

         //Prevent duplicate faces in 1 photo
         if (facesInPhoto.Count > 0) {
            facesInPhoto.Sort (cbSortScoreAndId);
            for (int i = 1; i < facesInPhoto.Count; i++) {
               if ((facesInPhoto[i].NameSrc & NameSource.ManualOrCorrected) != 0) continue;
               removeAlreadyAssignedIds (facesInPhoto, i);
            }
            //PW Must be activated later
            //removeMatchesBelowThreshold (dst); (nog niet af!)
         }

         //Export the faces
         for (int i = 0; i < facesInPhoto.Count; i++) {
            var f = facesInPhoto[i];
            if ((f.NameSrc & NameSource.ManualOrCorrected) == 0)
               f.AssignMatchesToNamesAndClearMatches ();
            f.UpdateNames (faceNames);
            if (f.CopyNeeded) {
               f.Export (ep.Record);
               if (!sameIndexOrNotExist && f.FaceStorageId >= 0) {
                  var key = f.FaceStorageId.ToString();
                  storages.CopyOldToCur (key, key);
               }
               ctx.Pipeline.HandleValue (ctx, "record/face", ep.Record);
            } else
               ctx.Skipped++;
         }

         EXIT_RTN:
         return null;
      }

      /// <summary>
      /// Fills the matches list in face with the N best matches if above threshold
      /// </summary>
      private void matchFace (DbFace face, TargetFaces targetFaces) {
         face.Matches = null;
         if ((face.NameSrc & NameSource.ManualOrCorrected) == 0) {
            if (face.Embeddings == null || face.Embeddings.Length == 0)
               throw new BMException ("Normal face [{0}] has no embeddings.", face.Id);

            var m = targetFaces.FindFaces (face, faceScorer);
            if (m != null) {
               if (m[0].Score >= threshold) face.Matches = m;
            }
         }
      }


      private static void removeAlreadyAssignedIds (List<DbFace> list, int pos) {
         DbFace face = list[pos];

         //The loop is needed, since we might remove the top match and will retry after that
         while (true) {
            if (face.Matches == null) goto EXIT_RTN;
            int nameId = face.GetMatchedNameId ();
            if (nameId < 0) goto EXIT_RTN;  //stop at matches to an unknown face

            for (int i = pos - 1; i >= 0; i--) {
               if (list[i].GetMatchedNameId () == nameId) goto REMOVE;
            }
            //Nothing to remove: exit loop
            break;

         REMOVE:
            float limit = face.Matches[0].Score * .8f;
            face.Matches.RemoveAt (0);
            if (face.Matches.Count == 0 || face.Matches[0].Score < limit) {
               face.Matches = null; //remove all matches
               break;
            }
         }

      EXIT_RTN:
         return;
      }

      private static int cbSortScoreAndId (DbFace a, DbFace b) {
         int rc = Comparer<float>.Default.Compare (b.GetMatchedScore (), a.GetMatchedScore ());
         return (rc != 0) ? rc : string.CompareOrdinal (a.Id, b.Id);
      }



      private void assignEmbedding (DbFace face) {
         if (face.Embeddings != null || face.FaceStorageId <= 0) return;
         var bytes = storages.OldEmbeddingStorage.GetBytes (face.FaceStorageId.ToString (), false);
         face.Embeddings = BufferHelper.FromByteArray<float> (bytes);
         if (!face.HasEmbeddings) {
            string msg = Invariant.Format ("Face [{0}] has no embeddings or Detected score.", face.Id);
            Logs.ErrorLog.Log (msg);
         }
      }

      private TargetFaces loadTargetFaces (PipelineContext ctx, List<DbFace> faces) {
         var targetFaces = new List<DbFace> ();
         var node = ctx.DatasourceAdmin.ContextNode;
         bool includeNonOK = node.ReadBool ("manual/@include_non_ok", false);
         float ratioLo = (float)node.ReadFloat ("manual/@ratio_range_lo", 0.7);
         float ratioHi = (float)node.ReadFloat ("manual/@ratio_range_hi", 1.3);

         var allUnknown = new List<DbFace> ();
         int manualKnown = 0;
         int manualUnknown = 0;
         int correctedKnown = 0;
         int correctedUnknown = 0;
         foreach (var f in faces) {
            if (!f.HasEmbeddings) {
               if (f.FaceCount > 0) {
                  string msg = Invariant.Format ("Error: known face [{0}] has no embeddings.", f.Id);
                  Logs.ErrorLog.Log (msg);
               }
               continue;
            }
            switch (f.NameSrc & NameSource.ManualOrCorrected) {
               default: continue;
               case NameSource.Manual:
                  if ((f.NameSrc & NameSource.Known) != 0) {
                     ++manualKnown;
                     if (f.Names.Count == 0) {
                        string msg = Invariant.Format ("No names for target face [{0}].", f.Id);
                        Logs.ErrorLog.Log (msg);
                        continue;
                     }
                  } else {
                     ++manualUnknown;
                     allUnknown.Add (f);
                  }

                  if (!includeNonOK && !f.FaceOK) continue;
                  if (f.FaceRatio < ratioLo || f.FaceRatio > ratioHi) continue;

                  targetFaces.Add (f);
                  continue;
               case NameSource.Corrected:
                  if ((f.NameSrc & NameSource.Known) != 0)
                     ++correctedKnown;
                  else {
                     ++correctedUnknown;
                     allUnknown.Add (f);
                  }
                  continue;
            }
         }

         //Update ID's for unknown faces.
         //-1 means its a simple corrected unknown face
         //For manual-unknown faces we assign a negative ID < -1. 
         int updated = 0;
         if (allUnknown.Count > 0) {
            int lowestID = -1;
            foreach (var f in allUnknown) {
               if ((f.NameSrc & NameSource.Manual) == 0) continue;
               int faceId = getFaceId (f);
               if (faceId < lowestID) lowestID = faceId;
            }
            ctx.ImportLog.Log ("LoadTargetFaces: collected lowest unknown ID={0}.", lowestID);
            foreach (var f in allUnknown) {
               int id = getFaceId (f);
               if ((f.NameSrc & NameSource.Manual) != 0) {
                  if (id != -1) continue;
                  id = --lowestID;
                  f.Names.Clear ();
                  f.Names.Add (new DbFaceName (id, 1.0f, null, faceNames.NameById (id)));
               } else {
                  if (f.Names.Count == 0) continue;
                  f.Names.Clear ();
               }
               f.CopyNeeded = true;
               ++updated;
            }
            ctx.ImportLog.Log ("LoadTargetFaces: assigned lowest unknown ID={0}.", lowestID);
         }

         ctx.ImportLog.Log ("LoadTargetFaces: faces={0}, include_non_ok={1} ratio={2} .. {3}",
                             faces.Count, includeNonOK, ratioLo, ratioHi);
         ctx.ImportLog.Log ("Stats about assigned faces:\nManual known={0}\nManualUnknown={1}\nCorrectedKnown={2}\nCorrectedUnknown={3}\nAll={4}",
                             manualKnown,
                             manualUnknown,
                             correctedKnown,
                             correctedUnknown,
                             faces.Count);
         ctx.ImportLog.Log ("Result: {0} target faces, {1} updated ID's for unknown faces.", targetFaces.Count, updated);

         return new TargetFaces (targetFaces);
      }

      private static int getFaceId (DbFace f) {
         return f.Names?.Count == 0 ? -1 : f.Names[0].Id;
      }


      private static int checkDuplicates (Logger logger, List<int> nameIds, List<string> ids) {
         if (nameIds.Count <= 1) return 0;
         int dups = 0;
         for (int i = 1; i < nameIds.Count; i++) {
            int id = nameIds[i];
            if (id < 0) continue;
            for (int j = i - 1; j >= 0; j--) {
               if (id == nameIds[j]) {
                  ++dups;
                  logger.Log ("-- id {0} found in [{1}] and in [{2}].", id, ids[i], ids[j]);
               }
            }
         }
         return dups;
      }
      public static void DumpDuplicateNames (Logger logger, IndexInfo index) {
         var req = index.CreateESRequest();
         req.Sort.Add (new ESSortField ("id", ESSortDirection.asc));
         int totalDups = 0;
         using (var e = new ESRecordEnum (req)) {
            var nameIds = new List<int> ();
            var ids = new List<string> ();
            string prev = null;
            foreach (var doc in e) {
               var id = doc.Id;
               int idx = id.LastIndexOf ('~');
               var mainId = id.Substring (0, idx);
               if (mainId != prev) {
                  prev = mainId;
                  totalDups += checkDuplicates (logger, nameIds, ids);
                  nameIds.Clear ();
                  ids.Clear ();
               }
               var names = doc._Source.ReadArr ("names", null);
               if (names == null) continue;

               ids.Add (id);
               nameIds.Add (((JsonObjectValue)names[0]).ReadInt ("id"));
            }
            totalDups += checkDuplicates (logger, nameIds, ids);
         }
         if (totalDups==0) {
            logger.Log (_LogType.ltInfo, "No records with duplicate names detected.");
         } else {
            logger.Log (_LogType.ltWarning, "{0} records had duplicate names.");
         }
      }
   }
}

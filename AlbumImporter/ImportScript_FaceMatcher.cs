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

using Bitmanager.AlbumTools;
using Bitmanager.Core;
using Bitmanager.Elastic;
using Bitmanager.ImportPipeline;
using Bitmanager.Json;
using Bitmanager.Xml;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.AccessControl;
using System.Text.RegularExpressions;
using System.Xml;

namespace AlbumImporter {

   public class ImportScript_FaceMatcher : ImportScriptBase {
      private List<DbFace> existingFaces;
      private FaceNames faceNames;
      private Storages storages;
      private float[] weightPerFaceCount;
      private Regex filter;

      private IFaceScorer faceScorer;
      private float threshold;

      public object OnDatasourceStart (PipelineContext ctx, object value) {
         Init (ctx, true);

         string fltr = ctx.DatasourceAdmin.ContextNode.ReadStr ("filter/@expr", null);
         if (fltr != null) filter = new Regex(fltr, RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

         //Load the facecount weights
         var weightNode = ctx.DatasourceAdmin.ContextNode.SelectMandatoryNode ("weight");
         this.threshold = (float)weightNode.ReadFloat ("@threshold");
         var min = (float)weightNode.ReadFloat ("@min_weight");
         var factors = new List<float> (20);
         factors.Add (0);
         factors.Add (1f);
         var logBase = Math.Log2(weightNode.ReadFloat ("@face_factor"));
         for (int i=1; i<100000; i++) {
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


         //Read all face names
         faceNames = ReadFaceNames ();

         logger.Log ("Fetching existing faces");
         string url = base.copyFromUrl;
         if (url == null) url = base.oldIndexUrl;
         existingFaces = new FaceCollection (ctx.ImportLog, url, false).GetFaces ();

         logger.Log ("Loading storages");
         storages = fullImport
            ? new Storages (faceAdminDir, newTimestamp, oldTimestamp)
            : new Storages (faceAdminDir, newTimestamp);


         logger.Log ("Loading embeddings from storage");
         foreach (var f in existingFaces) assignEmbedding (f);


         var targetFaces = loadTargetFaces (ctx, existingFaces, out var updatedFaces);

         ctx.ImportLog.Log ("Starting face matching. FullImport={0}, target faces={1}, updated faces={2}",
            fullImport,
            targetFaces.Count,
            updatedFaces.Count);

         var ep = ctx.Action.Endpoint;
         var facesInPhoto = new List<DbFace> (30);
         int idx;

         if (filter != null) existingFaces = filterFaces (ctx.ImportLog, existingFaces, filter);

         if (fullImport) {  //Full import: we need to emit *all* records and copy *all* face-imgs
            var idSet = fetchAllIds ();
            idx = 0;
            while (idx < existingFaces.Count) {
               loadAndMatchFacesForOnePhoto (targetFaces, facesInPhoto, existingFaces, ref idx);
               //skip no longer existing ID's
               var mainId = facesInPhoto[0].MainId;
               if (!idSet.Contains (mainId)) continue;

               for (int i = 0; i < facesInPhoto.Count; i++) {
                  ctx.IncrementEmitted ();
                  var f = facesInPhoto[i];
                  f.AssignMatchesToNamesAndClearMatches (faceScorer);
                  if (f.FaceStorageId > 0) {
                     string key = f.FaceStorageId.ToString ();
                     storages.CopyOldToCur (key, f.Id);
                  }
                  f.UpdateNames (faceNames);
                  f.Export (ep.Record);
                  ctx.Pipeline.HandleValue (ctx, "record", ep.Record);
               }
            }
         } else { //Incremental import: only emit assigned faces
            //Export updated faces
            for (int i = 0; i < updatedFaces.Count; i++) {
               ctx.IncrementEmitted ();
               updatedFaces[i].Export (ep.Record);
               ctx.Pipeline.HandleValue (ctx, "record", ep.Record);
            }
            idx = 0;
            while (idx < existingFaces.Count) {
               loadAndMatchFacesForOnePhoto (targetFaces, facesInPhoto, existingFaces, ref idx);

               for (int i = 0; i < facesInPhoto.Count; i++) {
                  ctx.IncrementEmitted ();
                  var f = facesInPhoto[i];
                  if (f.FaceCount == 0) continue;
                  if (f.NameSrc.IsManualDefined ()) continue;

                  bool needExport = f.AssignMatchesToNamesAndClearMatches (faceScorer);
                  needExport |= f.UpdateNames (faceNames);
                  if (needExport) {
                     f.Export (ep.Record);
                     ctx.Pipeline.HandleValue (ctx, "record", ep.Record);
                  }
               }
            }
         }
         return null;
      }

      private static List<DbFace> filterFaces (Logger logger, List<DbFace> faces, Regex expr) {
         var ret = new List<DbFace> ();
         foreach (var face in faces) {
            if (expr.IsMatch(face.Id)) ret.Add (face);
         }
         logger.Log ("Filtered faces by {0}: {1} out of {2}", expr, ret.Count, faces.Count);
         return ret;
      }

      public object OnDatasourceEnd (PipelineContext ctx, object value) {
         esConnection.CreateIndexRequest ().Refresh (newIndex);

         dumpNameUsage ();
         dumpDuplicateNames();


         handleExceptions = false;
         ctx.ImportLog.Log ("Closing storage file(s)");
         storages?.Dispose ();

         try {
            if (esConnection != null && newIndex != null) {
               var syncher = new StorageSyncher (ctx.ImportLog, esConnection, newIndex);
               syncher.Synchronize (faceAdminDir);
            }
         } catch (Exception e) {
            ctx.ImportLog.Log (e, "Failed to synchronize: {0}", e.Message);
         }
         return null;
      }



      private void matchFace (DbFace face, TargetFaces targetFaces) {
         face.Matches = null;
         if (!face.NameSrc.IsManualDefined ()) {
            if (face.Embeddings == null || face.Embeddings.Length == 0)
               throw new BMException ("Normal face [{0}] has no embeddings.", face.Id);

            var m = targetFaces.FindFaces (face, faceScorer);
            if (m != null) {
               face.Explain = faceScorer.Explain (face, m[0].MatchedFace);
               if (m[0].Score >= threshold) face.Matches = m;
            }
         }
      }

      private List<DbFace> loadAndMatchFacesForOnePhoto (TargetFaces targetFaces, List<DbFace> dst, List<DbFace> src, ref int pos) {
         if (pos >= src.Count) return null;
         DbFace face = src[pos];
         dst.Clear ();
         dst.Add (face);
         int faceCount = face.FaceCount;
         if (faceCount == 0) { pos++; return dst; }

         //Handle faces. The src list are sorted on mainID. So the #faceCount faces
         //are starting at pos and ending at pos+faceCount
         matchFace (face, targetFaces); //Handle 1st face
         int i = pos + 1;
         int end = pos + faceCount;
         for (; i < end; i++) {
            face = src[i];
            matchFace (face, targetFaces);
            dst.Add (face);
         }
         pos = end;

         if (faceCount > 1) {
            dst.Sort (cbSortScoreAndId);
            for (i = 1; i < dst.Count; i++) {
               removeAlreadyAssignedIds (dst, i);
            }
            //PW Must be activated later
            //removeMatchesBelowThreshold (dst); (nog niet af!)
         }
         return dst;
      }

      private static void removeAlreadyAssignedIds (List<DbFace> list, int pos) {
         DbFace face = list[pos];
         if (face.NameSrc.IsManualDefined ()) goto EXIT_RTN; 

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
         float scoreA = a.GetMatchedScore (), scoreB = b.GetMatchedScore ();
         int rc = Comparer<float>.Default.Compare (scoreB, scoreA);
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

      private TargetFaces loadTargetFaces (PipelineContext ctx, List<DbFace> faces, out List<DbFace> updatedFaces) {
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
               if (f.FaceCount>0) {
                  string msg = Invariant.Format ("Error: known face [{0}] has no embeddings.", f.Id);
                  Logs.ErrorLog.Log (msg);
               }
               continue;
            }
            switch (f.NameSrc & (NameSource.Manual | NameSource.Corrected)) {
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

         var upd = new List<DbFace> ();
         if (allUnknown.Count > 0) {
            allUnknown.Sort (cbSortUnknownFaces);
            int lowestID = getFaceId (allUnknown[0]);
            foreach (var f in allUnknown) {
               int id = getFaceId (f);
               if (id < -1) continue;

               upd.Add (f);
               id = --lowestID;
               f.Names.Clear ();
               f.Names.Add (new DbFaceName (id, 1.0f, null, faceNames.NameById (id)));
            }
         }

         ctx.ImportLog.Log ("LoadTargetFaces: faces={0}, include_non_ok={1} ratio={2} .. {3}",
                             faces.Count, includeNonOK, ratioLo, ratioHi);
         ctx.ImportLog.Log ("Stats about assigned faces:\nManual known={0}\nManualUnknown={1}\nCorrectedKnown={2}\nCorrectedUnknown={3}\nAll={4}",
                             manualKnown,
                             manualUnknown,
                             correctedKnown,
                             correctedUnknown,
                             faces.Count);
         ctx.ImportLog.Log ("Result: {0} target faces, {1} updated faces.", targetFaces.Count, upd.Count);

         updatedFaces = upd;
         return new TargetFaces (targetFaces);
      }

      private static int getFaceId(DbFace f) {
         return f.Names?.Count == 0 ? -1 : f.Names[0].Id;
      }

      /// <summary>
      /// Sort unknown faces ascending on face-id. Since these ID's are negative, the first item will be the lowest assigned ID
      /// </summary>
      private static int cbSortUnknownFaces(DbFace a, DbFace b) {
         return Comparer<int>.Default.Compare (getFaceId (a), getFaceId (b));
      }

      private HashSet<string> fetchAllIds () {
         var idReq = esConnection.CreateSearchRequest ("album-ids");
         idReq.SetSource ("id", null);
         var set = new HashSet<string> ();
         using (var idEnum = new ESRecordEnum (idReq)) {
            foreach (var doc in idEnum) set.Add (doc.ReadStr ("id"));
         }
         return set;
      }

      private void checkDuplicates (List<int> nameIds, List<string> ids) {
         if (nameIds.Count <= 1) return;
         for (int i = 1; i < nameIds.Count; i++) {
            int id = nameIds[i];
            if (id < 0) continue;
            for (int j = i - 1; j >= 0; j--) {
               if (id == nameIds[j]) {
                  logger.Log ("-- id {0} found in [{1}] and in [{2}].", id, ids[i], ids[j]);
               }
            }
         }
      }
      private void dumpDuplicateNames() {
         var req = esConnection.CreateSearchRequest (newIndex);
         req.Sort.Add (new ESSortField ("id", ESSortDirection.asc));
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
                  checkDuplicates (nameIds, ids);
                  nameIds.Clear ();
                  ids.Clear ();
               }
               var names = doc._Source.ReadArr ("names", null);
               if (names == null) continue;

               ids.Add (id);
               nameIds.Add (((JsonObjectValue)names[0]).ReadInt ("id"));
            }
            checkDuplicates (nameIds, ids);
         }

      }

      private void dumpNameUsage () {
         var req = esConnection.CreateSearchRequest (newIndex);
         req.TrackTotalHits = true;
         req.Query = new ESTermQuery ("src", "mk");
         var termsAgg = new ESTermsAggregation ("nameid", "names.id", faceNames.Count);
         var nestedAgg = new ESNestedAggregation ("nameid", "names", termsAgg);
         req.Size = 0;
         req.Aggregations.Add (nestedAgg);
         var resp = req.Search ();
         resp.ThrowIfError ();

         var terms = (ESTermsAggregationResult)resp.Aggregations.FindByName (true, "nameid", "nameid");
         var touched = new bool[faceNames.Count];
         logger.Log ("Dumping counters for {0} known faces", resp.TotalHits);
         foreach (var item in terms.GetSortedItems ()) {
            int id = Invariant.ToInt32 (item.GetKey ());
            logger.Log ("-- '{0}': {1}", faceNames.NameById (id), item.Count);
            if (id >= 0) touched[id] = true;
         }

         for (int i = 0; i < touched.Length; i++) {
            if (touched[i]) continue;
            logger.Log ("-- '{0}': 0", faceNames.NameById (i));
         }
      }
   }
}

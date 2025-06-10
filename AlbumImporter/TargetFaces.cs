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
//#define DBG
using Bitmanager.Core;

namespace AlbumImporter {
   public class TargetFaces {
      private readonly DbFace[] faces;
      public int Count => faces.Length;

      public TargetFaces (List<DbFace> list) {
         faces = list.ToArray ();
         Array.Sort (faces, SortId);
      }

      static int SortId (DbFace a, DbFace b) {
         if (b.Names[0].Id > a.Names[0].Id) return -1;
         if (b.Names[0].Id < a.Names[0].Id) return 1;
         return 0;
      }

      //Prefer highest score and lowest ID (assuming familar faces are lowest in the list)
      static int compareDetectedFace (FaceHit a, FaceHit b) {
         if (a.Score > b.Score) return 1;
         if (a.Score < b.Score) return -1;
         if (a.MatchedFace != null && b.MatchedFace != null) {
            int rc = b.MatchedFace.Names[0].Id - a.MatchedFace.Names[0].Id;
            if (rc != 0) return rc;
         }
         return 0;
      }

      public List<FaceHit> FindFaces (DbFace face, IFaceScorer scorer) {
         if (face == null) return null;
         var hits = new FixedPriorityQueue<FaceHit> (3, compareDetectedFace);
         int prevId = -1;
         float maxScore = -1;
         DbFace bestFace = null;
         float[] embeddings = face.Embeddings;
         for (int i = 0; i < faces.Length; i++) {
            var known = faces[i];
            var score = scorer.Score (face, known);
            if (score < .2f) continue;
            int nameId = known.Names[0].Id;
            if (nameId == prevId) {
               if (score > maxScore) {
                  maxScore = score;
                  bestFace = known;
               }
               continue;
            }
            if (bestFace != null) hits.Add (new FaceHit (bestFace, face, maxScore));
            maxScore = score;
            bestFace = known;
            prevId = nameId;
         }
         if (bestFace != null) hits.Add (new FaceHit (bestFace, face, maxScore));

         if (hits.Count == 0) return null;
         return hits.ToSortedList (true);
      }

   }



}


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

namespace AlbumImporter.FaceRecognition {
   public class TargetFaces {
      private readonly DbFace[] targets;
      public int Count => targets.Length;
      public DbFace[] Faces => targets;

      public TargetFaces (List<DbFace> list) {
         targets = list.ToArray ();
         Array.Sort (targets, SortId);
      }

      static int SortId (DbFace a, DbFace b) {
         if (b.Name.Id > a.Name.Id) return -1;
         if (b.Name.Id < a.Name.Id) return 1;
         return 0;
      }

      //Prefer highest score and lowest ID (assuming familar faces are lowest in the list)
      static int compareDetectedFace (FaceHit a, FaceHit b) {
         if (a.Score > b.Score) return 1;
         if (a.Score < b.Score) return -1;
         return b.MatchedFace.Name.Id - a.MatchedFace.Name.Id;
      }

      public List<FaceHit> FindFaces (DbFace face, IFaceScorer rawScorer, IFaceScorer scorer) {
         if (face == null) return null;
         var hits = new FixedPriorityQueue<FaceHit> (3, compareDetectedFace);
         int prevId = -1;
         float maxScore = -1;
         DbFace bestTarget = null;
         float[] embeddings = face.Embeddings;
         for (int i = 0; i < targets.Length; i++) {
            var target = targets[i];
            var score = rawScorer.Score (face, target);
            if (score < .2f) continue;
            int nameId = target.Name.Id;
            if (nameId == prevId) {
               if (score > maxScore) {
                  maxScore = score;
                  bestTarget = target;
               }
               continue;
            }
            if (bestTarget != null) hits.Add (new FaceHit (bestTarget, face, rawScorer));
            maxScore = score;
            bestTarget = target;
            prevId = nameId;
         }
         if (bestTarget != null) hits.Add (new FaceHit (bestTarget, face, rawScorer));

         if (hits.Count == 0) return null;
         var hitsList = hits.ToSortedList (true);
         for (int i = 0; i < hitsList.Count; i++)
            hitsList[i].Rescore (scorer);
         return hitsList;
      }

   }



}


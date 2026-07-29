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
using Bitmanager.Xml;
using System.Xml;

namespace AlbumImporter.FaceRecognition {
   /// <summary>
   /// Class to hold the thresholds that determine if an assigned face gets status=V(alid)
   /// </summary>
   public class FaceRecognitionThresholds {
      public readonly float ScoreThreshold;
      public readonly float MinScoreDiff;
      public readonly float MaxScoreDiffFactor;
      public const float DEF_SCORE_THRESHOLD = 0.25f;

      public FaceRecognitionThresholds(XmlNode node) {
         ScoreThreshold = (float)node.ReadFloat ("@score", DEF_SCORE_THRESHOLD);
         MinScoreDiff = (float)node.ReadFloat ("@min_diff", 0.03f);
         MaxScoreDiffFactor = (float)node.ReadFloat ("@max_diff_factor", 0.9f);
      }

      public override string ToString() {
         return Invariant.Format ("FaceThresholds [score={0:F2} min_diff={0:F2}, max_diff_factor={2:F2}]", ScoreThreshold, MinScoreDiff, MaxScoreDiffFactor);
      }
   }
}

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
using SimpleSimd;
using System.Text;

namespace AlbumImporter {

   public interface IFaceScorer {
      float Score (DbFace toMatched, DbFace matchCandidate);
      string Explain (DbFace toMatched, DbFace matchCandidate);
   }

   public class FaceScorer : IFaceScorer {
      private const int MAX_HEIGHT = 250;
      private static readonly float[] heightFactors;
      private readonly float[] faceCountFactors;
      private readonly float minFaceCountFactor;
      private StringBuilder sb = new StringBuilder();

      public FaceScorer (float[] faceCountFactors) {
         this.faceCountFactors = faceCountFactors;
         this.minFaceCountFactor = faceCountFactors.Length==0 ? 1 : faceCountFactors[^1];
      }

      //static float heightScore (int h) {
      //   return heightFactors [h< heightFactors.Length ? h : heightFactors.Length-1];
      //}

      private static readonly float log250 = (float)Math.Log10 (250);
      private static float heightScore (int h) {
         return (h >= 250) ? 1f : (float)(Math.Log10 (h) / log250);
      }
      private static float ratioScore (float ratio) {
         return (float)Math.Max (.4, Math.Pow (ratio >= 1f ? 2f - ratio : ratio, 0.25));
      }

      public virtual float Score (DbFace toMatch, DbFace matchCandidate) {
         float score = SimdOps<float>.Dot (toMatch.Embeddings, matchCandidate.Embeddings);
         int fc = toMatch.FaceCount;
         float f = fc <faceCountFactors.Length ? faceCountFactors[fc] : minFaceCountFactor;
         float h = heightScore (toMatch.H0 < matchCandidate.H0 ? toMatch.H0 : matchCandidate.H0);//(toMatch.H0+matchCandidate.H0)/2);
         float r = ratioScore (toMatch.FaceRatio);
         return score * f * h * r;
      }

      public virtual string Explain (DbFace toMatch, DbFace matchCandidate) {
         sb.Clear ();
         float score = SimdOps<float>.Dot (toMatch.Embeddings, matchCandidate.Embeddings);
         int fc = toMatch.FaceCount;
         float f = fc < faceCountFactors.Length ? faceCountFactors[fc] : minFaceCountFactor;
         int minH = toMatch.H0 < matchCandidate.H0 ? toMatch.H0 : matchCandidate.H0;
         float h = heightScore (minH);//(toMatch.H0+matchCandidate.H0)/2);
         float r = ratioScore (toMatch.FaceRatio);
         sb.AppendFormat (Invariant.Culture,
                         "s={0:F2} raw={1:F2}, f={2:F2}, h={3:F2}, r={4:F2}",
                         score * f* h * r, score, f, h, r);
         return sb.ToString ();
      }

      static FaceScorer () {
         float[] factors = new float[MAX_HEIGHT + 1];
         factors[0] = 0f;
         for (int i = 1; i < factors.Length; i++) {
            factors[i] = (float)(1f - Math.Log10 (i / (double)MAX_HEIGHT));
         }
         heightFactors = factors;
      }

   }

}

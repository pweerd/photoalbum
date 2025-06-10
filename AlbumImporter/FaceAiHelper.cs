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
using FaceAiSharp;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Drawing;
using Image = SixLabors.ImageSharp.Image;
using PointF = SixLabors.ImageSharp.PointF;
using RectangleF = SixLabors.ImageSharp.RectangleF;

namespace AlbumImporter {

   public class FaceHit {
      public readonly DbFace MatchedFace;
      public readonly DbFace FaceToMatch;
      public string Explain;
      public readonly float Score;
      public readonly int MatchedNameId;

      public FaceHit (DbFace matched, DbFace faceToMatch, float score) {
         MatchedFace = matched;
         FaceToMatch = faceToMatch;
         Score = score;
         MatchedNameId = matched.Names[0].Id;
      }
   }

   internal static class FaceDetectorResultExt {
      public const int EYE_L = 0;
      public const int EYE_R = 1;
      public const int NOSE = 2;
      public const int MOUTH_L = 3;
      public const int MOUTH_R = 4;
      public static float Score (this in FaceDetectorResult fdr) {
         return (float)fdr.Confidence;
      }

      /// <summary>
      /// Ratio is 0..2
      /// 1 means face is frontal
      /// Below 1 means face is en-profile
      /// Above 1 means face is looking up or down 
      /// </summary>
      public static float GetFaceRatio (this in FaceDetectorResult fdr) {
         var landmarks = fdr.Landmarks;
         if (landmarks.Count < 5) return float.NaN;

         float dx = Math.Max (0, landmarks[EYE_R].X - landmarks[EYE_L].X);
         float dy = ((landmarks[MOUTH_L].Y + landmarks[MOUTH_R].Y) - (landmarks[EYE_L].Y + landmarks[EYE_R].Y)) / 2;
         return (dy <= 0) ? 2f : Math.Min (2f, dx / dy);
      }

      public static int GetFaceAngle (this in FaceDetectorResult fdr) {
         const double TO_DEGREES = 180 / Math.PI;
         var landmarks = fdr.Landmarks;
         if (landmarks.Count < 5) return -1;


         double dy = (landmarks[EYE_L].Y + landmarks[EYE_R].Y) - (landmarks[MOUTH_L].Y + landmarks[MOUTH_R].Y);
         double dx = (landmarks[EYE_L].X + landmarks[EYE_R].X) - (landmarks[MOUTH_L].X + landmarks[MOUTH_R].X);
         double angle = Math.Atan(dy/dx) * TO_DEGREES;
         if (angle < 0) angle += 360;
         return (int)(angle + .5);

      }
   }



}

//PW
//public class DetectorResult {
//   public readonly RectangleF Box;
//   public readonly IReadOnlyList<PointF> LandmarksRO;
//   public readonly PointF[] Landmarks;
//   public readonly float Score;
//   public DetectorResult (FaceDetectorResult r) {
//      Box = r.Box;
//      LandmarksRO = r.Landmarks;
//      Landmarks = r.Landmarks.ToArray ();
//      Score = (float)r.Confidence;
//   }

//   /// <summary>
//   /// Ratio is 0..2
//   /// 1 means face is frontal
//   /// Below 1 means face is en-profile
//   /// Above 1 means face is looking up or down 
//   /// </summary>
//   public float FaceRatio {
//      get {
//         float ret = float.NaN;
//         if (LandmarksRO.Count >= 5) {
//            float dx = Math.Max (0, LandmarksRO[1].X - LandmarksRO[0].X);
//            float dy = ((LandmarksRO[3].Y + LandmarksRO[4].Y) - (LandmarksRO[1].Y + LandmarksRO[1].Y)) / 2;
//            ret = (dy <= 0) ? 2f : Math.Min (2f, dx / dy);
//         }
//         return ret;
//      }
//   }
//}


public class FaceAiHelper : IDisposable {
   private readonly IFaceDetector _faceDetector;
   private readonly IFaceEmbeddingsGenerator _embeddingsGenerator;
   public FaceAiHelper () {
      _faceDetector = FaceAiSharpBundleFactory.CreateFaceDetector ();
      _embeddingsGenerator = FaceAiSharpBundleFactory.CreateFaceEmbeddingsGenerator ();
   }

   public Image<Rgb24> LoadImage (byte[] bytes) {
      return Image.Load<Rgb24> (bytes);
   }
   public Image<Rgb24> LoadImage (Stream strm) {
      return Image.Load<Rgb24> (strm);
   }
   public Image<Rgb24> LoadImage (string fn) {
      return Image.Load<Rgb24> (fn);
   }


   public FaceDetectorResult[] DetectFaces (Image<Rgb24> img) {
      var coll = _faceDetector.DetectFaces (img);
      return coll.Count == 0 ? null : coll.ToArray ();
   }

   /// <summary>
   /// Rotates the image over multiples of 90 degrees. If the extraction-score is bestFactor better then the
   /// current score, this rotation is used.
   /// bestScore is introduced because otherwise one of the rotated images is used while the score is only
   /// very slightly better.
   /// Rotation is tried in this order: 0, 180, 270, 90 and so giving preference to the original img or the 
   /// 180 deg rotated img
   /// </summary>
   public FaceDetectorResult[] RotateAndDetectFaces (ref Image<Rgb24> img, float bestFactor, out RotateMode rotateMode) {
      ExtractResult best = new ExtractResult ();
      Image<Rgb24> clone = img.Clone ();
      try {
         extractAndSetBest (ref best, img, RotateMode.None, bestFactor);

         //We use the clone for all 3 rotations. This saves making a copy of the bitmap
         clone.Mutate (x => x.RotateFlip (RotateMode.Rotate180, FlipMode.None));
         extractAndSetBest (ref best, clone, RotateMode.Rotate180, bestFactor);

         clone.Mutate (x => x.RotateFlip (RotateMode.Rotate90, FlipMode.None));
         extractAndSetBest (ref best, clone, RotateMode.Rotate270, bestFactor);

         clone.Mutate (x => x.RotateFlip (RotateMode.Rotate180, FlipMode.None));
         extractAndSetBest (ref best, clone, RotateMode.Rotate90, bestFactor);

         rotateMode = best.RotateMode;
         if (best.TotalScore == 0f) return null;

         switch (best.RotateMode) {
            case RotateMode.None: break;
            case RotateMode.Rotate90: //is current state of the clone!
               img.Dispose ();
               img = clone;
               clone = null;
               break;
            default:
               img.Mutate (x => x.RotateFlip (best.RotateMode, FlipMode.None));
               break;
         }
         return best.Faces;
      } finally {
         clone?.Dispose ();
      }

   }

   struct ExtractResult {
      public readonly FaceDetectorResult[] Faces;
      public readonly float TotalScore;
      public readonly RotateMode RotateMode;
      public ExtractResult () {
         Faces = null;
         TotalScore = 0;
         RotateMode = RotateMode.None;
      }

      public ExtractResult (RotateMode rotateMode, FaceDetectorResult[] faces, float totalScore) {
         Faces = faces;
         TotalScore = totalScore;
         RotateMode = rotateMode;
      }
   }

   static int order;
   private void extractAndSetBest (ref ExtractResult best, Image<Rgb24> img, RotateMode rotateMode, float bestFactor) {
      var faces = DetectFaces(img);
      if (faces == null) return;
      var total = 0f;
      for (int i = 0; i < faces.Length; i++) total += (float)faces[i].Confidence;

#if DBG
         string imgName = Invariant.Format (@"z:\temp\{0}_{1}_{2}{3}.jpg",
            ++order, rotateMode, total,
            total > best.TotalScore * bestFactor ? "_best" : "");
         img.SaveAsJpeg (imgName);
#endif
      if (total > best.TotalScore * bestFactor) {
         best = new ExtractResult (rotateMode, faces, total);
      };
   }

   public int GetExifOrientation (Image<Rgb24> img) {
      return img.Metadata.ExifProfile != null && img.Metadata.ExifProfile.TryGetValue (ExifTag.Orientation, out var tmp)
         ? tmp.Value : 0;
   }

   public void RotateBasedOnExifValue (Image<Rgb24> img) {
      RotateBasedOnExifValue (img, GetExifOrientation (img));
   }

   public void RotateBasedOnExifValue (Image<Rgb24> img, int exifOrientation) {
      switch (exifOrientation) {
         case 2: //"Mirror horizontal")]
            img.Mutate (x => x.Flip (FlipMode.Horizontal));
            break;
         case 3: //"Rotate 180")]
            img.Mutate (x => x.Rotate (RotateMode.Rotate180));
            break;
         case 4: //"Mirror vertical")]
            img.Mutate (x => x.Flip (FlipMode.Vertical));
            break;
         case 5: //"Mirror horizontal and rotate 270 CW")]
            img.Mutate (x => x.Flip (FlipMode.Horizontal).Rotate (RotateMode.Rotate270));
            break;
         case 6: //"Rotate 90 CW")]
            img.Mutate (x => x.Rotate (RotateMode.Rotate90));
            break;
         case 7: //"Mirror horizontal and rotate 90 CW")]
            img.Mutate (x => x.Flip (FlipMode.Horizontal).Rotate (RotateMode.Rotate90));
            break;
         case 8: //"Rotate 270 CW")] 
            img.Mutate (x => x.Rotate (RotateMode.Rotate270));
            break;
      }
   }

   public bool HasFace (Stream mem) {
      using (var img = Image.Load<Rgb24> (mem))
         return HasFace (img);
   }
   public bool HasFace (Image<Rgb24> img) {
      var coll = _faceDetector.DetectFaces (img);
      return coll.Count > 0;
   }

   public void Align (Image<Rgb24> img, FaceDetectorResult r) {
      _embeddingsGenerator.AlignFaceUsingLandmarks (img, r.Landmarks);
   }

   public float[] CreateEmbedding (Image<Rgb24> img) {
      return _embeddingsGenerator.GenerateEmbedding (img);
   }
   public float Compare (float[] a, float[] b) {
      return FaceAiSharp.Extensions.GeometryExtensions.Dot (a, b);
   }

   public Bitmap ToBitmap (Image<Rgb24> img) {
      var mem = new MemoryStream ();
      img.SaveAsBmp (mem);
      mem.Position = 0;
      return (Bitmap)System.Drawing.Image.FromStream (mem, false, false);
   }

   public void Dispose () {
      (_faceDetector as IDisposable)?.Dispose ();
      (_embeddingsGenerator as IDisposable)?.Dispose ();
   }
}


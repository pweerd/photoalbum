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
using Bitmanager.Json;
using MetadataExtractor;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using System.Diagnostics;

namespace AlbumImporter.FaceRecognition {

   /// <summary>
   /// Defines the relative position of a face in the larger photo
   /// Coordinates will be encoded as shorts by multiplying the relative position [0..1] by MAX
   /// </summary>
   public readonly struct RelPos {
      private const int MAX=32000;
      private const float SQR_MAX=MAX*MAX;
      public readonly short X1, X2, Y1, Y2;
      public readonly short LeftEyeX, LeftEyeY;
      public readonly short RightEyeX, RightEyeY;
      public readonly short NoseX, NoseY;
      public readonly short LeftMouthX, LeftMouthY;
      public readonly short RightMouthX, RightMouthY;
      public RelPos () {
         X1 = 0;
         X2 = 0;
         Y1 = 0;
         Y2 = 0;
         LeftEyeX = 0;
         LeftEyeY = 0;
         RightEyeX = 0;
         RightEyeY = 0;
         NoseX = 0;
         NoseY = 0;
         LeftMouthX = 0;
         LeftMouthY = 0;
         RightMouthX = 0;
         RightMouthY = 0;
      }

      public RelPos (in RectangleF rc, IReadOnlyList<PointF> landmarks, int divisor) {
         var factor = ((double)MAX) / divisor;
         X1 = (short)(factor * rc.Left + .5);
         X2 = (short)(factor * rc.Right + .5);
         Y1 = (short)(factor * rc.Top + .5);
         Y2 = (short)(factor * rc.Bottom + .5);
         LeftEyeX = (short)(factor * landmarks[FaceDetectorResultExt.EYE_L].X + .5);
         LeftEyeY = (short)(factor * landmarks[FaceDetectorResultExt.EYE_L].Y + .5);
         RightEyeX = (short)(factor * landmarks[FaceDetectorResultExt.EYE_R].X + .5);
         RightEyeY = (short)(factor * landmarks[FaceDetectorResultExt.EYE_R].Y + .5);
         NoseX = (short)(factor * landmarks[FaceDetectorResultExt.NOSE].X + .5);
         NoseY = (short)(factor * landmarks[FaceDetectorResultExt.NOSE].Y + .5);
         LeftMouthX = (short)(factor * landmarks[FaceDetectorResultExt.MOUTH_L].X + .5);
         LeftMouthY = (short)(factor * landmarks[FaceDetectorResultExt.MOUTH_L].Y + .5);
         RightMouthX = (short)(factor * landmarks[FaceDetectorResultExt.MOUTH_R].X + .5);
         RightMouthY = (short)(factor * landmarks[FaceDetectorResultExt.MOUTH_R].Y + .5);
      }

      public RelPos (JsonValue v) : this() {
         if (v==null) return;

         var arr = v as JsonArrayValue;
         if (arr != null) {
            if (arr.Count != 4 && arr.Count != 14) throw new BMException ("Incorrect length for relpos-array: {0}", arr.Count);
            X1 = (short)arr[0].AsInt ();
            X2 = (short)arr[1].AsInt ();
            Y1 = (short)arr[2].AsInt ();
            Y2 = (short)arr[3].AsInt ();
            if (arr.Count == 14) {
               LeftEyeX = (short)arr[4].AsInt ();
               LeftEyeY = (short)arr[5].AsInt ();
               RightEyeX = (short)arr[6].AsInt ();
               RightEyeY = (short)arr[7].AsInt ();
               NoseX = (short)arr[8].AsInt ();
               NoseY = (short)arr[9].AsInt ();
               LeftMouthX = (short)arr[10].AsInt ();
               LeftMouthY = (short)arr[11].AsInt ();
               RightMouthX = (short)arr[12].AsInt ();
               RightMouthY = (short)arr[13].AsInt ();
            }
            return;
         }

         //Backward compat mode. String contains x,y,w,h
         var str = ((JsonStringValue)v).AsString();
         var strArr = str.Split(',');
         if (strArr.Length != 4) throw new BMException ("Incorrect relpos-array: [{0}]", str);
         X1 = (short)(MAX * Invariant.ToFloat (strArr[0]) + .5f);
         Y1 = (short)(MAX * Invariant.ToFloat (strArr[1]) + .5f);
         X2 = (short)(X1 + (MAX * Invariant.ToFloat (strArr[2]) + .5f));
         Y2 = (short)(Y1 + (MAX * Invariant.ToFloat (strArr[3]) + .5f));
      }

      public bool IsEmpty => X1 == X2;
      public int GetOverlap (in RelPos other) {
         int w = Math.Min (X2, other.X2) - Math.Max (X1, other.X1);
         if (w <= 0) return 0;

         int h = Math.Min (Y2, other.Y2) - Math.Max (Y1, other.Y1);
         return (h <= 0) ? 0 : w * h;
      }
      public int GetArea () {
         return (X2 - X1) * (Y2 - Y1);
      }
      public float GetRelArea () {
         return ((X2 - X1) * (Y2 - Y1)) / SQR_MAX;
      }

      public override string ToString () {
         return Invariant.Format (@"{{{0:D5}, {1:D5}, {2:D5}, {3:D5}}}", X1, X2, Y1, Y2);
      }

      public JsonArrayValue ToJson() {
         var ret = new JsonArrayValue();
         ret.Add (X1);
         ret.Add (X2);
         ret.Add (Y1);
         ret.Add (Y2);
         ret.Add (LeftEyeX);
         ret.Add (LeftEyeY);
         ret.Add (RightEyeX);
         ret.Add (RightEyeY);
         ret.Add (NoseX);
         ret.Add (NoseY);
         ret.Add (LeftMouthX);
         ret.Add (LeftMouthY);
         ret.Add (RightMouthX);
         ret.Add (RightMouthY);
         return ret;
      }
      public float GetFaceRatio() {
         float dx = Math.Max (0, RightEyeX - LeftEyeX);
         float dy = ((LeftMouthY + RightMouthY) - (LeftEyeY + RightEyeY)) / 2;
         return (dy <= 0) ? 2f : Math.Min (2f, dx / dy);

      }
   }
   /// <summary>
   /// Represents a face stored in the DB
   /// Note that we don't store the embeddings in the ES index, since records will be big and slow
   /// The embeddings are stored in a separate storage file
   /// A face is keyed like &lt;main-key&gt;~&lt;num&gt;
   /// </summary>

   [DebuggerDisplay ("Face: {NameSrc}, {Name}")]
   public class DbFace {
      private static readonly DateTime updateStamp = DateTime.UtcNow;
      public string Id;
      public string User;
      public RelPos RelPos;
      public DateTime Updated;
      public DbFaceName Name;
      public float[] Embeddings;
      public List<FaceHit> Matches;
      public NameSource NameSrc;
      public float Confidence;
      public int FaceCount;
      /// <summary>
      /// NB FaceStorageId might contain a new or an old storID. Depending on the context. See ImportScript_FaceExtract::combineExistingFaces
      /// </summary>
      public int FaceStorageId;
      public int W0, H0;
      public int FaceAngleRaw;
      public int FaceAngle => FaceAngleRaw < 0 ? -1 : (((FaceAngleRaw + 45) / 90) % 4) * 90;
      public float FaceRatio;
      public float FaceRatio_1 {
         get {
            if (float.IsNaN (FaceRatio)) return float.NaN;
            return (FaceRatio >= 1f) ? (2f - FaceRatio) : FaceRatio;
         }
      }
      public RotateMode FaceOrientation;
      public bool FaceOK;
      public bool CopyNeeded;

      public DbFace () {
         CopyNeeded = true; //set default: if any face was added other than from GenericDocument, this value is true, 
         Updated = updateStamp;
         FaceAngleRaw = -1;
         FaceStorageId = -1;
      }
      public DbFace (string id): this() {
         Id = id;
      }

      public DbFace (GenericDocument rec, bool copyNeeded) : this(rec.Id) {
         CopyNeeded = copyNeeded;
         var src = rec._Source;
         Updated = src.ReadDate ("updated", updateStamp);
         FaceCount = src.ReadInt ("count", 0);
         User = src.ReadStr ("user", null);
         W0 = src.ReadInt ("w0", 0);
         H0 = src.ReadInt ("h0", 0);
         FaceAngleRaw = src.ReadInt ("face_angle_raw", -1);
         NameSrc = NameSourceExtensions.FromString(src.ReadStr ("src", null));
         FaceStorageId = src.ReadInt ("storage_id", -1);
         Confidence = src.ReadFloat ("confidence", 0);
         FaceRatio = src.ReadFloat ("face_ratio", float.NaN);
         FaceOK = src.ReadBool ("face_ok", true);
         FaceOrientation = (RotateMode)src.ReadInt ("FaceOrientation", 0);

         RelPos = new RelPos (src["relpos"]);

         var arr = src.ReadArr ("names", null);
         if (arr != null && arr.Count>0) { //Backward compatibility
            Name = new DbFaceName ((JsonObjectValue)arr[0], this);
         } else {
            var tmp = src.ReadObj("name", null);
            if (tmp != null) Name = new DbFaceName (tmp, this);
         }

         arr = src.ReadArr ("embeddings", null);
         if (arr != null && arr.Count != 0) {
            Embeddings = new float[arr.Count];
            for (int i = 0; i < arr.Count; i++)
               Embeddings[i] = (float)arr[i];
         }
      }


      public string MainId {
         get {
            int ix = Id.LastIndexOf ('~');
            return ix < 0 ? Id : Id.Substring (0, ix);
         }
      }

      public bool HasEmbeddings => Embeddings != null && Embeddings.Length > 0;


      public void UpdateNames (FaceNames definedNames) {
         if (Name != null && Name.Id >= 0) {
            var oldName = Name.Name;
            Name.UpdateName (definedNames);
            if (!string.Equals (oldName, Name.Name, StringComparison.Ordinal))
               CopyNeeded = true;
         }
      }

      public int GetMatchedNameId () {
         if ((NameSrc & NameSource.ManualOrCorrected) != 0)
            return Name != null ? Name.Id : -1;
         return Matches == null ? -1 : Matches[0].MatchedFace.Name.Id;
      }
      public float GetMatchedScore () {
         //Make sure we sort by manual -> corrected -> other
         if ((NameSrc & NameSource.ManualOrCorrected) != 0) {
            return (NameSrc & NameSource.Manual) != 0 ? 1 : 0.999f;
         }
         return Matches == null ? 0 : Matches[0].Score;
      }


      public void AssignMatchesToNamesAndClearMatches(bool rebuild, bool includeAltName) {
         if (Matches==null) {
            NameSrc = NameSource.NotAssigned;
            if (Name != null) {  //The face was assigned, but now it isn't anymore...
               MarkUpdated ();
               Name = null;
            }
            goto EXIT_RTN;
         }

         var m = Matches[0];
         NameSrc = m.MatchedFace.NameSrc.ToAuto();
         if (Name != null && Name.Id == m.MatchedFace.Name.Id && Name.Score == m.Score && !rebuild) goto EXIT_RTN;

         MarkUpdated ();
         Name = new DbFaceName (Matches, includeAltName);

      EXIT_RTN:
         Matches = null;
      }



      public void Export (JsonObjectValue rec) {
         rec.Clear ();
         rec["_id"] = Id;
         rec["id"] = Id;
         if (User != null) rec["user"] = User;
         if (Id[^1] == '0' && Id[^2] == '~') rec["first"] = true;
         if (Updated != DateTime.MinValue) rec["updated"] = Updated;
         rec["count"] = FaceCount;
         if (FaceCount > 0) {
            rec["w0"] = W0;
            rec["h0"] = H0;
            rec["any_face"] = true;
            rec["storage_id"] = FaceStorageId;
            rec["relpos"] = RelPos.ToJson();
            rec["relarea"] = Math.Round (RelPos.GetRelArea (), 4);
            if (!float.IsNaN (FaceRatio)) {
               rec["face_ratio"] = FaceRatio;
               rec["face_ratio_1"] = FaceRatio_1;
            }
            rec["face_ok"] = FaceOK;
            if (FaceOrientation != RotateMode.None) rec["face_orientation"] = (int)FaceOrientation;

            if (FaceAngleRaw >= 0) {
               rec["face_angle"] = FaceAngle;
               rec["face_angle_raw"] = FaceAngleRaw;
            }

            rec["src"] = NameSourceExtensions.ToString(NameSrc);
            if (Name != null) rec["name"] = Name.ToJson ();

            rec["confidence"] = Math.Round (Confidence, 2);
            rec["face_ratio2"] = RelPos.GetFaceRatio();
            //The embeddings will never be exported 
            //Instead they are saved in a separate storage file
         }
      }

      internal void MarkUpdated () {
         Updated = updateStamp;
         CopyNeeded = true;
      }


      /// <summary>
      /// Find the most overlapping item from the list of (existing) faces from the same photo.
      /// It is best to call this method from a backwards loop.
      /// </summary>
      public int FindMostOverlapping (List<DbFace> list) {
         if (RelPos.IsEmpty) return -1;
         int bestIndex = -1;
         float bestOverlap = 0;
         float ourArea = RelPos.GetArea ();

         //Loop backwards, since our caller will do that too and the lists of faces probably
         //have the same order
         for (int i=list.Count; i>0;) {
            --i;
            float fraction = RelPos.GetOverlap (list[i].RelPos) / Math.Max(ourArea, list[i].RelPos.GetArea());
            if (fraction > bestOverlap) {
               bestOverlap = fraction;
               bestIndex = i;
            }
         }
         if (bestIndex >= 0) {
            if (bestOverlap < .7f) bestIndex = -1;
         }
         return bestIndex;
      }
   }
}

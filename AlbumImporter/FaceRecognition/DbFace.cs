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
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace AlbumImporter.FaceRecognition {

   /// <summary>
   /// Defines the relative position of a face in the larger photo
   /// Coordinates will be encoded as shorts by multiplying the relative position [0..1] by MAX
   /// </summary>
   public readonly struct RelPos {
      private const int MAX=32000;
      public readonly short X1, X2, Y1, Y2;
      public RelPos () {
         X1 = 0;
         X2 = 0;
         Y1 = 0;
         Y2 = 0;
      }
      public RelPos (float x1, float x2, float y1, float y2) {
         X1 = (short)(MAX * x1 + .5f);
         X2 = (short)(MAX * x2 + .5f);
         Y1 = (short)(MAX * y1 + .5f);
         Y2 = (short)(MAX * y2 + .5f);
      }
      public RelPos (JsonValue v) {
         if (v==null) {
            X1 = 0;
            X2 = 0;
            Y1 = 0;
            Y2 = 0;
            return;
         }
         var arr = v as JsonArrayValue;
         if (arr != null) {
            if (arr.Count != 4) throw new BMException ("Incorrect length for relpos-array: {0}", arr.Count);
            X1 = (short)arr[0].AsInt ();
            X2 = (short)arr[1].AsInt ();
            Y1 = (short)arr[2].AsInt ();
            Y2 = (short)arr[3].AsInt ();
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

      public override string ToString () {
         return Invariant.Format (@"{{{0:D5}, {1:D5}, {2:D5}, {3:D5}}}", X1, X2, Y1, Y2);
      }

      public JsonArrayValue ToJson() {
         var ret = new JsonArrayValue();
         ret.Add (X1);
         ret.Add (X2);
         ret.Add (Y1);
         ret.Add (Y2);
         return ret;
      }
   }
   /// <summary>
   /// Represents a face stored in the DB
   /// Note that we don't store the embeddings in the ES index, since records will be big and slow
   /// The embeddings are stored in a separate storage file
   /// A face is keyed like &lt;main-key&gt;~&lt;num&gt;
   /// </summary>
   public class DbFace {
      private static readonly DateTime updateStamp = DateTime.UtcNow;
      public string Id;
      public string User;
      public RelPos RelPos;
      public DateTime Updated;
      public List<DbFaceName> Names;
      public float[] Embeddings;
      public List<FaceHit> Matches;
      public NameSource NameSrc;
      public int FaceCount;
      /// <summary>
      /// NB FaceStorageId might contain a new or an old storID. Depending on the context. See ImportScript_FaceExtract::combineExistingFaces
      /// </summary>
      public int FaceStorageId;
      public int W0, H0;
      public int FaceAngleRaw, FaceAngle;
      public float FaceRatio;
      public RotateMode FaceOrientation;
      public bool FaceOK;
      public bool CopyNeeded;

      public DbFace () {
         Names = new List<DbFaceName> (1);
         CopyNeeded = true; //set default: if any face was added other than from GenericDocument, this value is true, 
         Updated = updateStamp;
         FaceAngleRaw = -1;
         FaceAngle = -1;
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
         FaceAngle = src.ReadInt ("face_angle", -1);
         FaceAngleRaw = src.ReadInt ("face_angle_raw", -1);
         NameSrc = NameSourceExtensions.FromString(src.ReadStr ("src", null));

         FaceStorageId = src.ReadInt ("storage_id", -1);
         RelPos = new RelPos (src["relpos"]);

         var arr = src.ReadArr ("names", null);
         if (arr != null) {
            for (int i=0; i<arr.Count; i++)
               Names.Add (new DbFaceName ((JsonObjectValue)arr[i]));
         }
         arr = src.ReadArr ("embeddings", null);
         if (arr != null && arr.Count != 0) {
            Embeddings = new float[arr.Count];
            for (int i=0; i<arr.Count;i++)
               Embeddings[i] = (float)arr[i];
         }
         FaceRatio = src.ReadFloat ("face_ratio", float.NaN);
         FaceOK = src.ReadBool ("face_ok", true);
         FaceOrientation = (RotateMode)src.ReadInt ("FaceOrientation", 0);
      }


      public string MainId {
         get {
            int ix = Id.LastIndexOf ('~');
            return ix < 0 ? Id : Id.Substring (0, ix);
         }
      }

      public bool HasEmbeddings => Embeddings != null && Embeddings.Length > 0;


      public void UpdateNames (FaceNames definedNames) {
         for (int i = 0; i < Names.Count; i++) {
            if (Names[i].Id < 0) continue;
            string oldName = Names[i].Name;
            Names[i].UpdateName (definedNames);
            if (!string.Equals (oldName , Names[i].Name , StringComparison.Ordinal)) {
               CopyNeeded = true;
            }
         }
      }

      public int GetMatchedNameId () {
         if ((NameSrc & NameSource.ManualOrCorrected) != 0)
            return Names != null && Names.Count > 0 ? Names[0].Id : -1;
         return Matches == null ? -1 : Matches[0].MatchedNameId;
      }
      public float GetMatchedScore () {
         //Make sure we sort by manual -> corrected -> other
         if ((NameSrc & NameSource.ManualOrCorrected) != 0) {
            return (NameSrc & NameSource.Manual) != 0 ? 1 : 0.999f;
         }
         return Matches == null ? 0 : Matches[0].Score;
      }


      public void AssignMatchesToNamesAndClearMatches() {
         if (Matches==null) {
            NameSrc = NameSource.NotAssigned;
            if (Names.Count > 0) {  //The face was assigned, but now it isn't anymore...
               MarkUpdated ();
               Names.Clear ();
            }
            goto EXIT_RTN;
         }

         NameSrc = Matches[0].MatchedFace.NameSrc.ToAuto();
         if (hasChangeInNameIds()) {
            if (Names.Count==0 || Names[0].Id != Matches[0].MatchedNameId)
               MarkUpdated ();
            Names.Clear ();
            for (int i = 0; i < Matches.Count; i++) {
               var m = Matches[i];
               Names.Add (new DbFaceName (m.MatchedNameId,
                                        m.Score,
                                        m.Explain,
                                        null));
            }
         }

      EXIT_RTN:
         Matches = null;
      }


      private bool hasChangeInNameIds() {
         if (Matches.Count != Names.Count) return true;
         for (int i=0; i<Matches.Count; i++) {
            if (Names[i].Id != Matches[i].MatchedNameId || Names[i].Score != Matches[i].Score)
               return true;
         }
         return false;
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
            if (!float.IsNaN(FaceRatio)) rec["face_ratio"] = FaceRatio;
            rec["face_ok"] = FaceOK;
            if (FaceOrientation != RotateMode.None) rec["face_orientation"] = (int)FaceOrientation;

            if (FaceAngleRaw >= 0) {
               rec["face_angle"] = FaceAngle;
               rec["face_angle_raw"] = FaceAngleRaw;
            }

            rec["src"] = NameSourceExtensions.ToString(NameSrc);
            if (Names?.Count > 0) {
               var arr = new JsonArrayValue ();
               foreach (var f in Names) {
                  arr.Add (f.ToJson ());
                  break; //PW only export 1 name...
               }
               rec["names"] = arr;
            }
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

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
using MathNet.Numerics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
//using System.Drawing;

namespace AlbumImporter {

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
      public string RelPos;
      public DateTime Updated;
      public List<DbFaceName> Names;
      public float[] Embeddings;
      public List<FaceHit> Matches;
      public string Explain;
      public NameSource NameSrc;
      public int FaceCount;
      public int FaceStorageId;
      public int W0, H0;
      public int FaceAngleRaw, FaceAngle;
      public float FaceRatio;
      public RotateMode FaceOrientation;
      public bool FaceOK;

      public DbFace () {
         Names = new List<DbFaceName> (1);
         Updated = updateStamp;
         FaceAngleRaw = -1;
         FaceAngle = -1;
      }
      public DbFace (string id): this() {
         Id = id;
      }

      public DbFace (GenericDocument rec) : this(rec.Id) {
         var src = rec._Source;
         Updated = src.ReadDate ("updated", updateStamp);
         FaceCount = src.ReadInt ("count", 0);
         User = src.ReadStr ("user", null);
         W0 = src.ReadInt ("w0", 0);
         H0 = src.ReadInt ("h0", 0);
         FaceAngle = src.ReadInt ("face_angle", -1);
         FaceAngleRaw = src.ReadInt ("face_angle_raw", -1);
         Explain = src.ReadStr ("explain", null);
         NameSrc = NameSourceExtensions.FromString(src.ReadStr ("src", null));

         FaceStorageId = src.ReadInt ("storage_id", -1);
         //InnerStr = src.ReadStr ("rect", "");
         RelPos = src.ReadStr ("relpos", "");

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


      public bool UpdateNames (FaceNames definedNames) {
         bool ret = false;
         for (int i = 0; i < Names.Count; i++) {
            if (Names[i].Id < 0) continue;
            string oldName = Names[i].Name;
            Names[i].UpdateName (definedNames);
            if (!string.Equals (oldName, Names[i].Name, StringComparison.Ordinal)) ret = true;
         }
         return ret;
      }

      public int GetMatchedNameId () {
         if (NameSourceExtensions.IsManualDefined(NameSrc))
            return Names != null && Names.Count > 0 ? Names[0].Id : -1;
         return Matches == null ? -1 : Matches[0].MatchedNameId;
      }
      public float GetMatchedScore () {
         if (NameSourceExtensions.IsManualDefined (NameSrc))
            return Names != null && Names.Count > 0 ? 1 : 0;
         return Matches == null ? 0 : Matches[0].Score;
      }


      public bool AssignMatchesToNamesAndClearMatches(IFaceScorer faceScorer) {
         bool ret = false;
         if (NameSourceExtensions.IsManualDefined (NameSrc)) goto EXIT_RTN;

         //PW
         ret = true; //Force record to be always written
         if (Matches==null) {
            NameSrc = NameSource.NotAssigned;
            if (Names.Count > 0) {
               ret = true;
               MarkUpdated ();
               Names.Clear ();
            }
            goto EXIT_RTN;
         }

         NameSrc = Matches[0].MatchedFace.NameSrc.ToAuto();
         if (hasChangeInNameIds()) {
            ret = true;
            if (Names.Count==0 || Names[0].Id != Matches[0].MatchedNameId)
               MarkUpdated ();
            Names.Clear ();
            for (int i = 0; i < Matches.Count; i++) {
               var m = Matches[i];
               Names.Add (new DbFaceName (m.MatchedNameId,
                                        m.Score,
                                        faceScorer.Explain(m.FaceToMatch, m.MatchedFace),
                                        null));
            }
         }

      EXIT_RTN:
         Matches = null;
         return ret;
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
            rec["relpos"] = RelPos;
            if (!float.IsNaN(FaceRatio)) rec["face_ratio"] = FaceRatio;
            rec["face_ok"] = FaceOK;
            if (FaceOrientation != RotateMode.None) rec["face_orientation"] = (int)FaceOrientation;

            if (FaceAngleRaw >= 0) {
               rec["face_angle"] = FaceAngle;
               rec["face_angle_raw"] = FaceAngleRaw;
            }

            if (Explain != null) rec["explain"] = Explain;

            rec["src"] = NameSourceExtensions.ToString(NameSrc);
            if (Names?.Count > 0) {
               var arr = new JsonArrayValue ();
               foreach (var f in Names) {
                  arr.Add (f.ToJson ());
                  break; //PW only export 1...
               }
               rec["names"] = arr;
            }
            //The embeddings will never be exported 
            //Instead they are saved in a separate storage file
         }
      }

      private static bool shouldExportNames (NameSource ns) {
         return NameSourceExtensions.IsKnown (ns);
      }


      internal void MarkUpdated () {
         Updated = updateStamp;
      }

      /// <summary>
      /// Returns the coordinates of the face in relative positions (1 means the width of the original photo)
      /// </summary>
      RectangleF GetRectangle () {
         return RelPos.ToRectangle (1f);
      }

      /// <summary>
      /// Find the most overlapping item from the list of (existing) faces from the same photo
      /// </summary>
      public int FindMostOverlapping (List<DbFace> list) {
         RectangleF ours = GetRectangle ();
         if (ours.Width ==0f) return -1;
         int bestIndex = -1;
         float bestArea = 0f;
         for (int i=0; i<list.Count; i++) {
            RectangleF theirs = list[i].GetRectangle ();
            float a = ours.Overlap (theirs);
            if (a <= bestArea) continue;
            bestArea = a;
            bestIndex = i;
         }
         if (bestIndex >= 0) {
            if (bestArea / ours.Area() < .7f) bestIndex = -1;
         }
         return bestIndex;
      }
   }
}

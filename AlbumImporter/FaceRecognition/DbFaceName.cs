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
using Bitmanager.Json;
using System.Text.RegularExpressions;

namespace AlbumImporter.FaceRecognition {
   
   /// <summary>
   /// Represents a known or a matched face
   /// </summary>
   public class DbFaceName {
      public string Name, AltName;
      public readonly string Explain;
      public readonly int Id, AltId;
      public readonly float Score;
      public readonly float ScoreRaw, AltScore;
      public float Confidence;


      /// <summary>
      /// Constructor, used for 'unknown' faces
      /// </summary>
      public DbFaceName (int id) {
         Id = id;
         Score = 0f;
         ScoreRaw = 0f;
         AltId = -1;
      }

      public DbFaceName (List<FaceHit> matches, bool includeAltName) {
         var m = matches[0];
         Id = m.MatchedFace.Name.Id;
         Score = m.Score;
         ScoreRaw = m.ScoreRaw;
         Explain = m.Explain;
         Confidence = m.MatchedFace.Confidence;

         AltId = -1;
         if (matches.Count > 1) {
            var m2 = matches[1];
            AltScore = m2.ScoreRaw;
            if (includeAltName) AltId = m2.MatchedFace.Name.Id;
         }
         //Name(s) will be set later via UpdateName()
      }

      public DbFaceName (JsonObjectValue v, DbFace parent) {
         AltId = -1;
         Id = v.ReadInt("id");
         if ((parent.NameSrc & NameSource.ManualOrCorrected) != 0) {
            Confidence = parent.Confidence;
            ScoreRaw = 1;
            if ((parent.NameSrc & NameSource.Unknown) != 0)
               Score = 0;
            else 
               Score = 1f;
         } else {
            Score = v.ReadFloat ("match_score", float.NaN);
            ScoreRaw = v.ReadFloat ("match_score_raw", Score);
            Confidence = v.ReadFloat ("confidence", 0f);
         }

         AltScore = v.ReadFloat ("altscore", 0f);
         AltId = v.ReadInt ("altid", -1);
         if (AltId>=0) AltName = v.ReadStr ("altname", null);

         Name = v.ReadStr ("name", null);
         Explain = v.ReadStr ("explain", null);
      }

      public bool UpdateName (FaceNames faceNames) {
         var ret = false;
         if (Id >= 0) {
            var oldName = Name;
            Name = faceNames.NameById (Id);
            ret = string.Equals (Name, oldName, StringComparison.Ordinal);
            if (AltId >= 0) {
               oldName = AltName;
               AltName = faceNames.NameById (AltId);
               ret = string.Equals (AltName, oldName, StringComparison.Ordinal);
            }
         }
         return ret;
      }

      public JsonObjectValue ToJson() {
         var json = new JsonObjectValue();
         json.Add ("id", Id);
         json.Add ("match_score", Score);
         json.Add ("match_score_raw", ScoreRaw);
         json.Add ("confidence", Confidence);
         if (AltScore > 0) {
            json.Add ("diff_factor", Math.Round (AltScore / ScoreRaw, 3));
            json.Add ("diff", Math.Round (ScoreRaw - AltScore, 3));
            json.Add ("altscore", AltScore);
            json.Add ("altid", AltId);
            json.Add ("altname", AltName);
         }
         if (!string.IsNullOrEmpty (Explain)) json.Add ("explain", Explain);
         if (!string.IsNullOrEmpty(Name)) json.Add ("name", Name);
         return json;
      }

      public override string ToString () {
         return Invariant.Format("{0} ({1}, raw={2}, alt={3}", Name, Score, ScoreRaw, AltScore);
      }

   }
}

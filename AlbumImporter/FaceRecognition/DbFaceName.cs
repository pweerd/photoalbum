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
using Bitmanager.Json;
using System.Text.RegularExpressions;

namespace AlbumImporter.FaceRecognition {
   
   /// <summary>
   /// Represents a known or a matched face
   /// </summary>
   public class DbFaceName {
      public string Name;
      public readonly string Explain;
      public readonly int Id;
      public readonly float Score;
      public readonly float ScoreRaw;
      public float Confidence;


      /// <summary>
      /// Constructor, used for 'unknown' faces
      /// </summary>
      public DbFaceName (int id) {
         Id = id;
         Score = 0f;
         ScoreRaw = 0f;
      }
      //PW kan weg?
      public DbFaceName (int id, float score, string explain, string name) {
         Id = id;
         Score = score;
         ScoreRaw = score;
         Explain = explain;
         Name = name;
      }
      public DbFaceName (FaceHit match) {
         Id = match.MatchedFace.Name.Id;
         Score = match.Score;
         ScoreRaw = match.ScoreRaw;
         Explain = match.Explain;
         Confidence = match.MatchedFace.Confidence;
         //Name will be copied later
      }
      public DbFaceName (JsonObjectValue v, DbFace parent) {
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
         Name = v.ReadStr ("name", null);
         Explain = v.ReadStr ("explain", null);
      }

      public void UpdateName (FaceNames faceNames) {
         if (Id >= 0) 
            Name = faceNames.NameById(Id);
      }

      public JsonObjectValue ToJson() {
         var json = new JsonObjectValue();
         json.Add ("id", Id);
         json.Add ("match_score", Score);
         json.Add ("match_score_raw", ScoreRaw);
         json.Add ("confidence", Confidence);
         if (!string.IsNullOrEmpty (Explain)) json.Add ("explain", Explain);
         if (!string.IsNullOrEmpty(Name)) json.Add ("name", Name);
         return json;
      }

   }
}

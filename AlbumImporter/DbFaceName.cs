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

namespace AlbumImporter {
   
   /// <summary>
   /// Represents a known or a matched face
   /// </summary>
   public class DbFaceName {
      public string Name;
      public string Explain;
      public readonly int Id;
      public float Score;

      public DbFaceName (int id, float score, string explain, string name) {
         Id = id;
         Score = score;
         Explain = explain;
         Name = name;
      }
      public DbFaceName (JsonObjectValue v) {
         Id = v.ReadInt("id");
         Score = v.ReadFloat ("match_score", float.NaN);
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
         if (!string.IsNullOrEmpty (Explain)) json.Add ("explain", Explain);
         if (!string.IsNullOrEmpty(Name)) json.Add ("name", Name);
         return json;
      }

   }
}

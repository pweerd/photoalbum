/*
 * Copyright © 2024, De Bitmanager
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

namespace BMAlbum {
   public class FaceNames {
      public readonly FaceName[] Names;
      public readonly FaceName[] SortedNames;
      public readonly int Count;
      private static int changeId;
      public readonly int ChangeId;

      public FaceNames (string fn) {
         ChangeId = changeId++;
         var list = new List<FaceName> ();
         int i = 0;
         foreach (string line in File.ReadLines (fn)) {
            list.Add (new FaceName (i, line.Trim ()));
            i++;
         }
         Names = list.ToArray ();
         list.Sort ((a, b) => StringComparer.OrdinalIgnoreCase.Compare (a.Name, b.Name));
         SortedNames = list.ToArray ();
         Count = Names.Length;
      }

      public string NameById (int id) => Names[id].Name; 
   }

   public class FaceName {
      public readonly int Id;
      public readonly string Name;
      public FaceName (int id, string name) {
         Id = id;
         Name = name;
      }
   }

}

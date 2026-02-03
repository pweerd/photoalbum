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
using Bitmanager.Elastic;
using Bitmanager.Storage;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AlbumImporter.FaceRecognition {

   /// <summary>
   /// Collection of faces that are read from the DB (face index)
   /// </summary>
   public class FaceCollection {
      private readonly Dictionary<string, DbFace> dict;
      public int LargestStorageId { get; private set; }
      public int LargestFaceId { get; private set; }
      public int Count => dict.Count;

      public FaceCollection() {
         dict = new Dictionary<string, DbFace>(10000);
      }
      public FaceCollection(Logger logger, IndexInfo index, bool copyNeeded) 
         :this() {
         Load(logger, index, copyNeeded);
      }

      public void Load(Logger logger, IndexInfo index, bool copyNeeded) {
         if (index == null) return;
         logger?.Log("Loading faces data from {0}", index.Url);
         var req = index.CreateESRequest ();
         req.Query = new ESExistsQuery ("count");
         ////Only load the faces that were assigned some value.
         //req.Query = new ESExistsQuery ("src");

         int largestStorId = 0;
         int largestFaceId = 0;
         int oldCount = dict.Count;
         using (var recs = new ESRecordEnum(req)) {
            foreach (var rec in recs) {
               //if (rec.Id.StartsWith (@"D\2009-02-22 Jan en Jeannet 25 jaar\20090222 152351  Jan en Jeannet 25 jaar.JPG~1"))
               //   Debugger.Break ();

               var face = new DbFace (rec, copyNeeded);
               if (face.FaceStorageId > largestStorId) largestStorId = face.FaceStorageId;
               if (face.Names != null)
                  foreach (var name in face.Names)
                     if (name.Id > largestFaceId) largestFaceId = name.Id;
               dict.TryAdd(face.Id, face);
            }
         }
         if (largestStorId > LargestStorageId) LargestStorageId = largestStorId;
         if (largestFaceId > LargestFaceId) LargestFaceId = largestFaceId;

         logger?.Log("Loaded {0} faces data from {1}. CopyNeeded={2}", dict.Count - oldCount, index.Url, copyNeeded);
      }

      public bool TryGetValue (string id, out DbFace face) {
         return dict.TryGetValue (id, out face);
      }

      private List<DbFace> _cached;
      public List<DbFace> GetFaces () {
         if (_cached == null) {
            _cached = dict.Values.ToList ();
            _cached.Sort(SortOnId);
         }
         return _cached;
      }

      public static int SortOnId (DbFace a, DbFace b) {
         return string.CompareOrdinal (a.Id, b.Id);
      }


      public static List<DbFace> GetExistingFacesForId (List<DbFace> existingFaces, string id) {
         List<DbFace> ret = null;

         int i = -1;
         int j = existingFaces.Count;
         //Invariant: existingFaces[i] < id && existingFaces[J] >= id
         while (i+1<j) {
            int m = (i + j) / 2;
            if (string.CompareOrdinal (existingFaces[m].Id, id) < 0)
               i = m;
            else j = m;
         }

         //j is now pointing at the first existingFaces[J] >= id
         int len = id.Length;
         for (; j< existingFaces.Count; j++) {
            string key = existingFaces[j].Id;
            if (key.Length < len + 2) break;
            if (key[len] != '~') break;
            if (!key.StartsWith (id, StringComparison.Ordinal)) break;

            if (ret==null) ret = new List<DbFace> ();
            ret.Add (existingFaces[j]);
         }
         return ret;
      }
   }
}

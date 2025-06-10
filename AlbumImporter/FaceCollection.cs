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

namespace AlbumImporter {

   /// <summary>
   /// Collection of faces that are read from the DB (face index)
   /// </summary>
   public class FaceCollection {
      private readonly Dictionary<string, DbFace> dict;
      public readonly string FingerPrint;
      public readonly int LargestStorageId;
      public readonly int LargestFaceId;
      public int Count => dict.Count;

      public FaceCollection (Logger logger, string url, bool onlyManualOrCorrected=false) {
         dict = new Dictionary<string, DbFace> ();
         if (logger != null) logger.Log ("Loading Faces data from {0}.", url);
         if (url == null) goto EXIT_RTN;
         
         var req = Utils.CreateESRequest (url);

         //Filter out admin or error records
         var bq = new ESBoolQuery ();
         bq.AddNot (new ESExistsQuery ("type"));
         req.Query = bq;

         FingerPrint = Utils.GetIndexFingerPrint (req.Connection, req.IndexName);
         if (FingerPrint == null) goto EXIT_RTN;

         int largestStorId = 0;
         int largestFaceId = 0;
         using (var e = new ESRecordEnum (req)) {
            foreach (var rec in e) {
               var face = new DbFace (rec);
               if (onlyManualOrCorrected && !face.NameSrc.IsManualDefined()) continue;
               if (face.FaceStorageId > largestStorId) largestStorId = face.FaceStorageId;
               if (face.Names != null) 
                  foreach (var name in face.Names)
                     if (name.Id > largestFaceId) largestFaceId = name.Id;
               dict.Add (face.Id, face);
            }
         }
         LargestStorageId = largestStorId;
         LargestFaceId = largestFaceId;

      EXIT_RTN:
         if (logger != null) logger.Log ("Loaded {0} Face items from {1}. Largest storageId={2}", dict.Count, url, LargestStorageId);
      }

      public bool TryGetValue (string id, out DbFace face) {
         return dict.TryGetValue (id, out face);
      }

      private List<DbFace> _cached;
      public List<DbFace> GetFaces () {
         if (_cached == null) {
            _cached = dict.Values.ToList ();
            _cached.Sort((a,b) => string.CompareOrdinal(a.Id, b.Id));
         }
         return _cached;
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

         int len = id.Length;
         for (; j< existingFaces.Count; j++) {
            string key = existingFaces[j].Id;
            if (key.Length < len + 2) break;
            if (!key.StartsWith (id, StringComparison.Ordinal)) break;
            if (key[len] != '~') break;

            if (ret==null) ret = new List<DbFace> ();
            ret.Add (existingFaces[j]);
         }
         return ret;
      }
   }
}

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
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AlbumImporter {
   /// <summary>
   /// Helper class to remove all storages that don't have an associated index in Elastic
   /// </summary>
   public class StorageSyncher {
      private readonly Logger logger;
      private readonly List<string> presentTimestamps;

      public StorageSyncher (Logger logger, string esHost, string index) 
         : this (logger, new ESConnection(esHost), index) {
      }
      public StorageSyncher (Logger logger, ESConnection conn, string index) {
         this.logger = logger;
         logger.Log ("Synching storages with present indexes: fetching indexes");
         var resp = conn.Send (HttpMethod.Get, getIndexMaskReqUrl(index));
         var json = resp.ThrowIfError ().Json;
         var list = new List<string> ();
         foreach (var key in json.Keys) {
            logger.Log ("-- Present index: {0}", key);
            //Save timestamp, including the '_'
            list.Add (getTimestamp (key));
         }
         presentTimestamps = list;
      }

      public void Synchronize (string root) {
         removeSuperflouisStorages (root, "album-faces_*.stor");
         removeSuperflouisStorages (root, "album-embeddings_*.stor");
      }

      private static string getTimestamp (string name) {
         return name.Substring (name.Length - 16, 16);
      }
      private static string getIndexMaskReqUrl (string name) {
         return name.Substring (0, name.Length - 15) + "*/_settings";
      }

      private bool isPresent (string fn) {
         string x = getTimestamp (Path.GetFileNameWithoutExtension (fn));
         return this.presentTimestamps.Contains (x);
      }

      private void delete (string fn) {
         try {
            File.Delete (fn);
            logger.Log ("-- Deleted [{0}]", fn);
         } catch (Exception e) {
            logger.Log (_LogType.ltError, "-- Delete [{0}] failed: {1}", fn, e.Message);
         }
      }
      private void removeSuperflouisStorages (string root, string spec) {
         foreach (var f in Directory.GetFiles (root, spec)) {
            bool isValid = isPresent (f);
            logger.Log ("detected [{0}]: present={1}", f, isValid);
            if (!isValid) {
               delete (f);
               delete (Path.ChangeExtension (f, ".idx"));
            }
         }
      }
   }
}

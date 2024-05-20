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

using Bitmanager.Core;
using Bitmanager.Storage;
using Bitmanager.Xml;
using System.Xml;

namespace BMAlbum { 
   public class NamedStorage: FileStorage {
      public readonly string Name;
      public NamedStorage(string fn): base (fn, FileOpenMode.Read) {
         Name = Path.GetFileNameWithoutExtension (fn);
      }
   }

   /// <summary>
   /// Holds administration about individual faces, like:
   /// - translation from name to id and vv.
   /// - storage file for the face image. This storage file is hard-coupled to the face index-name
   /// </summary>
   public class FacesAdmin: IDisposable {
      const string FN_FACENAMES = "faceNames.txt";
      private readonly Logger logger;
      private readonly string root;
      private readonly FileSystemWatcher namesWatcher;
      private volatile FaceNames names;
      private volatile NamedStorage storage;

      public FaceNames Names {
         get {
            if (names != null) return names;
            lock (root) {
               if (names != null) return names;
               return names = new FaceNames (Path.Combine (root, FN_FACENAMES));
            }
         }
      }

      public FacesAdmin (string dir, Logger siteLog) {
         logger = siteLog;
         root = Path.GetFullPath (dir);
         string fn = Path.Combine (root, FN_FACENAMES);
         if (File.Exists (fn)) names = new FaceNames (fn);
         namesWatcher = new FileSystemWatcher (root, FN_FACENAMES);
         namesWatcher.NotifyFilter = NotifyFilters.LastWrite;
         namesWatcher.Changed += onNamesChanged;
         namesWatcher.Renamed += onNamesRenamed;
         namesWatcher.EnableRaisingEvents = true;
      }
      public FacesAdmin (XmlNode node, Logger siteLog) : this (node.ReadPath ("@dir"), siteLog) { }

      public FileStorage GetStorage (string indexName) {
         var tmp = storage;
         if (tmp != null && tmp.Name == indexName) return tmp;

         lock (root) {
            tmp = storage;
            if (tmp != null && tmp.Name == indexName) return tmp;

            string fn = Path.Combine (root, indexName + ".stor");
            storage = new NamedStorage (fn);
            tmp?.Dispose ();
            logger.Log ("Loaded face storage [{0}]", fn);
            return storage;
         }
      }

      public FileStorage GetStorage () {
         return storage;
      }

      private void onNamesChanged (object sender, FileSystemEventArgs e) {
         try {
            names = new FaceNames (e.FullPath);
         } catch (Exception ex) {
            Logs.ErrorLog.Log (ex, "Face names [{0}] changed, but error during reload: {1}", e.FullPath, ex.Message);
         }
         logger.Log ("Face names [{0}] reloaded.", e.FullPath);
      }

      private void onNamesRenamed (object sender, RenamedEventArgs e) {
         onNamesChanged (sender, e);
      }

      public void Dispose () {
         storage?.Dispose ();
         namesWatcher?.Dispose ();
      }
   }
}

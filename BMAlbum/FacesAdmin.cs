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
using Bitmanager.IO;
using Bitmanager.Storage;
using Bitmanager.Web;
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
      private volatile FaceNames names;
      private volatile NamedStorage storage;

      public FaceNames Names => names;
      public FileStorage Storage => storage;

      public FacesAdmin (XmlNode node, Logger siteLog) : this (node.ReadPath ("@dir"), siteLog) { }

      public FacesAdmin (string dir, Logger siteLog) {
         logger = siteLog;
         root = Path.GetFullPath (dir);
         createFaceNames ();
         WebGlobals.Instance.GlobalChangeRepository.RegisterFileWatcher (root,
            false,
            new NameFilter (FN_FACENAMES + "$", true),
            ChangeType.RenamedOrChanged,
            Events.EV_FACENAMES_CHANGED,
            onNamesChanged
         );
      }

      public void Dispose () {
         storage?.Dispose ();
      }


      public FileStorage CheckStorage (string indexName) {
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


      private FaceNames createFaceNames () {
         lock (root) {
            if (names != null) return names;
            return names = new FaceNames (Path.Combine (root, FN_FACENAMES));
         }
      }


      private void onNamesChanged (string key, object context) {
         if (key == Events.EV_FACENAMES_CHANGED) {
            createFaceNames ();
            logger.Log (_LogType.ltInformational, "Face names reloaded from [{0}].", Path.Combine (root, FN_FACENAMES));
         }
      }
   }
}

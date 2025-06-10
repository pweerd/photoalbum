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
using Bitmanager.Storage;

namespace AlbumImporter {
   /// <summary>
   /// Holds the old/new embeddings- and face storages
   /// NB: if there is no old storage, it is set to the current storage
   ///     This prevents logic to choose from which storage we need to copy
   /// </summary>
   public class Storages : IDisposable {
      public readonly FileStorage CurrentEmbeddingStorage, OldEmbeddingStorage, CurrentFaceStorage, OldFaceStorage;

      public Storages (string root, string curTimestamp, string oldTimestamp) {
         string fn;
         fn = Path.Combine (root, "album-faces" + curTimestamp + ".stor");
         CurrentFaceStorage = new FileStorage (fn, FileOpenMode.Create);
         fn = Path.Combine (root, "album-embeddings" + curTimestamp + ".stor");
         CurrentEmbeddingStorage = new FileStorage (fn, FileOpenMode.Create);

         fn = Path.Combine (root, "album-faces" + oldTimestamp + ".stor");
         OldFaceStorage = File.Exists (fn) ? new FileStorage (fn, FileOpenMode.Read): CurrentFaceStorage;
         fn = Path.Combine (root, "album-embeddings" + oldTimestamp + ".stor");
         OldEmbeddingStorage = File.Exists (fn) ? new FileStorage (fn, FileOpenMode.Read): CurrentEmbeddingStorage;
      }
      public Storages (string root, string curTimestamp) {
         string fn;
         fn = Path.Combine (root, "album-faces" + curTimestamp + ".stor");
         CurrentFaceStorage = new FileStorage (fn, FileOpenMode.ReadWrite);
         fn = Path.Combine (root, "album-embeddings" + curTimestamp + ".stor");
         CurrentEmbeddingStorage = new FileStorage (fn, FileOpenMode.ReadWrite);

         OldFaceStorage = CurrentFaceStorage;
         OldEmbeddingStorage = CurrentEmbeddingStorage;
      }

      public void CopyOldToCur(string key, string id) {
         copyOldToCur (CurrentEmbeddingStorage, OldEmbeddingStorage, key, id);
         copyOldToCur (CurrentFaceStorage, OldFaceStorage, key, id);
      }
      private void copyOldToCur (FileStorage dst, FileStorage src, string key, string id) {
         if (dst == src) throw new BMException ("Cannot copy storage-entry: src and dst are the same instance!");
         var entry = src.GetFileEntry (key);
         if (entry == null) throw new BMException ("Name-Id [{0}] not found in [{1}].", key, id);
         var bytes = src.GetBytes (entry);
         dst.AddBytes (bytes, key, entry.Modified, (CompressMethod)entry.CompressMethod);
      }


      public void Dispose () {
         CurrentEmbeddingStorage?.Dispose ();
         CurrentFaceStorage?.Dispose ();
         OldEmbeddingStorage?.Dispose ();
         OldFaceStorage?.Dispose ();

      }
   }

}

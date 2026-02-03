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
using Bitmanager.Json;

namespace AlbumImporter.PHash {
   internal class PHashCollection {
      private readonly Dictionary<string,PHashItemItem> dict;
      public readonly string IndexFingerPrint;
      public int Count => dict.Count;

      public PHashCollection() {
         dict = new Dictionary<string, PHashItemItem>(10000);
      }
      public PHashCollection(Logger logger, IndexInfo index, bool copyNeeded)
         : this() {
         Load (logger, index, copyNeeded);
      }

      public void Load(Logger logger, IndexInfo index, bool copyNeeded) {
         if (index == null) return;
         logger?.Log("Loading perceptual hashes from {0}", index.Url);
         var req = index.CreateESRequest ();
         int oldCount = dict.Count;
         using (var recs = new ESRecordEnum(req)) {
            foreach (var rec in recs) {
               var fp = new PHashItemItem (rec, copyNeeded);
               dict.TryAdd(fp.Id, fp);
            }
         }
         logger?.Log("Loaded {0} perceptual hashes from {1}. CopyNeeded={2}", dict.Count - oldCount, index.Url, copyNeeded);
      }

      public bool TryGetValue(string id, out PHashItemItem value) {
         return dict.TryGetValue(id, out value);
      }

   }

   public class PHashItemItem {
      public readonly string Id;
      public readonly ulong PHash1, PHash2;
      public readonly DateTime Ts;
      public readonly bool CopyNeeded;

      public PHashItemItem(string id, ulong ph1, ulong ph2) {
         Id = id;
         PHash1 = ph1;
         PHash1 = ph2;
         Ts = DateTime.UtcNow;
      }
      public PHashItemItem(GenericDocument doc, bool copyNeeded) {
         Id = doc.Id;
         CopyNeeded = copyNeeded;
         PHash1 = ulong.Parse(doc._Source.ReadStr("ph1"), System.Globalization.NumberStyles.HexNumber);
         PHash2 = ulong.Parse(doc._Source.ReadStr("ph2"), System.Globalization.NumberStyles.HexNumber);
         Ts = doc._Source.ReadDate("ts", DateTime.MinValue);
      }
      public void Save (JsonObjectValue rec) {
         rec["_id"] = Id;
         rec["ts"] = Ts;
         rec["ph1"] = PHash1.ToString("X");
         rec["ph2"] = PHash2.ToString("X");
      }

   }

}

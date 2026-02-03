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
using Bitmanager.ImportPipeline;
using Bitmanager.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AlbumImporter.Captions {

   public class Caption {
      public readonly string Id;
      public readonly DateTime Ts;
      public readonly string Caption_EN;
      public readonly string Caption_NL;
      public readonly string Prompt;
      public readonly int Temperature;
      public readonly bool CopyNeeded;
      public readonly bool Failed;
      public Caption (GenericDocument doc, bool copyNeeded) {
         Id = doc.Id;
         Failed = doc._Source.ReadBool ("failed", false);
         if (!Failed) {
            Caption_EN = doc._Source.ReadStr ("text_en");
            Caption_NL = doc._Source.ReadStr ("text_nl");
         }
         Ts = doc._Source.ReadDate("ts", DateTime.MinValue);
         Prompt = doc._Source.ReadStr("prompt", null);
         Temperature = doc._Source.ReadInt ("temperature", -1);
         CopyNeeded = copyNeeded;
      }

      public void Export (JsonObjectValue dst) {
         dst["_id"] = Id;
         dst["ts"] = Ts;
         dst["text_en"] = Caption_EN;
         dst["text_nl"] = Caption_NL;
         if (Prompt != null) dst["prompt"] = Prompt;
         if (Temperature != -1) dst["temperature"] = Temperature;
         if (Failed) dst["failed"] = true;

      }
   }

   /// <summary>
   /// Collection of photo captions that are read from the caption index
   /// Caption records are keyed by the same key as the main index
   /// </summary>
   public class CaptionCollection {
      private readonly Dictionary<string, Caption> dict;
      public int Count => dict.Count;

      public CaptionCollection () {
         dict = new Dictionary<string, Caption> (10000);
      }
      public CaptionCollection(Logger logger, IndexInfo index, bool copyNeeded)
         : this() {
         Load(logger, index, copyNeeded);
      }


      public void Load (Logger logger, IndexInfo index, bool copyNeeded) {
         if (index == null) return;
         logger?.Log("Loading caption data from {0}", index.Url);
         var req = index.CreateESRequest ();
         int oldCount = dict.Count;
         using (var recs = new ESRecordEnum(req)) {
            foreach (var rec in recs) {
               var caption = new Caption (rec, copyNeeded);
               dict.TryAdd(caption.Id, caption);
            }
         }
         logger?.Log("Loaded {0} caption data from {1}. CopyNeeded={2}", dict.Count- oldCount, index.Url, copyNeeded);
      }

      public bool TryGetValue (string id, out Caption value) {
         return dict.TryGetValue (id, out value);
      }

   }
}

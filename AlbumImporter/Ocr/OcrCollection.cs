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
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AlbumImporter.Ocr {

   public class OcrText {
      private static readonly Regex expr = new Regex ("\\p{L}{4}", RegexOptions.CultureInvariant | RegexOptions.Compiled);

      public readonly string Id;
      public readonly string Text;
      public readonly DateTime Ts;
      public readonly bool Valid;
      public readonly bool CopyNeeded;
      public OcrText (GenericDocument doc, bool copyNeeded) {
         Id = doc.Id;
         Ts = doc._Source.ReadDate ("ts", DateTime.MinValue);
         Text = doc._Source.ReadStr ("text", null);
         Valid = Text != null && expr.IsMatch (Text);
         CopyNeeded = copyNeeded;
      }
   }

   /// <summary>
   /// Collection of OCR texts that are read from the OCR database
   /// OCR records are keyed by the same key as the main index
   /// </summary>
   public class OcrCollection {
      private readonly Dictionary<string, OcrText> dict;
      public int Count => dict.Count;
      public OcrCollection() {
         dict = new Dictionary<string, OcrText>(10000);
      }
      public OcrCollection(Logger logger, IndexInfo index, bool copyNeeded, bool all)
         : this() {
         Load (logger, index, copyNeeded, all);
      }

      public void Load(Logger logger, IndexInfo index, bool copyNeeded, bool all) {
         if (index == null) return;
         logger?.Log("Loading OCR data from {0}", index.Url);
         var req = index.CreateESRequest ();
         if (!all) req.Query = new ESExistsQuery("text");
         int oldCount = dict.Count;
         using (var recs = new ESRecordEnum(req)) {
            foreach (var rec in recs) {
               var ocr = new OcrText (rec, copyNeeded);
               dict.TryAdd(ocr.Id, ocr);
            }
         }
         logger?.Log("Loaded {0} OCR data from {1}. CopyNeeded={2}", dict.Count - oldCount, index.Url, copyNeeded);
      }


      public bool TryGetValue (string id, out OcrText ocrText) {
         return dict.TryGetValue (id, out ocrText);
      }

   }
}

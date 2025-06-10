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
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AlbumImporter {

   public class Caption {
      public readonly string Id;
      public readonly DateTime Ts;
      public readonly string Caption_EN;
      public readonly string Caption_NL;
      public Caption (GenericDocument doc) {
         Id = doc.Id;
         Caption_EN = doc._Source.ReadStr ("text_en");
         Caption_NL = doc._Source.ReadStr ("text_nl");
         Ts = doc._Source.ReadDate ("ts", DateTime.MinValue);
      }
   }

   /// <summary>
   /// Collection of photo captions that are read from the caption index
   /// Caption records are keyed by the same key as the main index
   /// </summary>
   public class CaptionCollection {
      private readonly Dictionary<string, Caption> dict;
      public readonly string FingerPrint;
      public int Count => dict.Count;

      public CaptionCollection (Logger logger, string url) {
         dict = new Dictionary<string, Caption> ();
         if (logger != null) logger.Log ("Loading caption data from {0}", url);

         if (url != null) {
            var req = Utils.CreateESRequest (url);
            FingerPrint = Utils.GetIndexFingerPrint (req.Connection, req.IndexName);
            if (FingerPrint != null) {
               using (var recs = new ESRecordEnum (req)) {
                  foreach (var rec in recs) {
                     var caption = new Caption (rec);
                     dict.Add (caption.Id, caption);
                  }
               }
            }         
         }
         if (logger != null) logger.Log ("Loaded {0} caption items from {1}", dict.Count, url);
      }

      public void Load (Logger logger, IDataEndpoint _ep) {
         int oldCount = dict.Count;
         var ep = _ep as ESDataEndpoint;
         if (ep == null) return;

         string index = ep.DocType.Index.IndexName;
         var c = ep.Connection;
         //return index == null ? null : Utils.GetIndexFingerPrint (ep.Connection, index);

         if (logger != null) logger.Log ("Loading caption items from {0}/{1}", c.BaseUri, index);
         var req = c.CreateSearchRequest (index);
         using (var e = new ESRecordEnum (req)) {
            e.AcceptIndexNotExist = true;
            foreach (var rec in new ESRecordEnum (req)) {
               var caption = new Caption (rec);
               if (!dict.ContainsKey (caption.Id)) {
                  dict.Add (caption.Id, caption);
               }
            }
         }
         if (logger != null) logger.Log ("Loaded {0} extra caption items", dict.Count-oldCount);
      }

      public bool TryGetValue (string id, out Caption value) {
         return dict.TryGetValue (id, out value);
      }

   }
}

using Bitmanager.Core;
using Bitmanager.Elastic;
using Bitmanager.ImportPipeline;
using Bitmanager.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AlbumImporter {
   internal class PHashCollection {
      private readonly Dictionary<string,PHashItemItem> dict;
      public readonly string IndexFingerPrint;
      public int Count => dict.Count;

      public PHashCollection(Logger logger, string url) {
         dict = new Dictionary<string, PHashItemItem>();
         if (logger != null) logger.Log("Loading perceptual hashes from {0}", url);

         if (url != null) {
            var req = Utils.CreateESRequest (url);
            IndexFingerPrint = Utils.GetIndexFingerPrint(req.Connection, req.IndexName);
            if (IndexFingerPrint != null) {
               using (var recs = new ESRecordEnum(req)) {
                  foreach (var rec in recs) {
                     var fp = new PHashItemItem (rec);
                     dict.Add(fp.Id, fp);
                  }
               }
            }
         }
         if (logger != null) logger.Log("Loaded {0} perceptual hashes from {1}", dict.Count, url);

      }

      public void Load(Logger logger, IDataEndpoint _ep) {
         int oldCount = dict.Count;
         var ep = _ep as ESDataEndpoint;
         if (ep == null) return;

         string index = ep.DocType.Index.IndexName;
         var c = ep.Connection;

         if (logger != null) logger.Log("Loading perceptual hashes from {0}/{1}", c.BaseUri, index);
         var req = c.CreateSearchRequest (index);
         using (var e = new ESRecordEnum(req)) {
            e.AcceptIndexNotExist = true;
            foreach (var rec in new ESRecordEnum(req)) {
               var fp = new PHashItemItem (rec);
               dict.TryAdd(fp.Id, fp);
            }
         }
         if (logger != null) logger.Log("Loaded {0} extra perceptual hashes", dict.Count - oldCount);
      }

      public bool TryGetValue(string id, out PHashItemItem value) {
         return dict.TryGetValue(id, out value);
      }

   }

   public class PHashItemItem {
      public readonly string Id;
      public readonly ulong PHash1, PHash2;
      public readonly DateTime Ts;

      public PHashItemItem(string id, ulong ph1, ulong ph2) {
         Id = id;
         PHash1 = ph1;
         PHash1 = ph2;
         Ts = DateTime.UtcNow;
      }
      public PHashItemItem(GenericDocument doc) {
         Id = doc.Id;
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

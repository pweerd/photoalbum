using Bitmanager.Core;
using Bitmanager.Ocr;
using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AlbumImporter {
   public class PhotoFaceCounters {
      private readonly Dictionary<string, PhotoFaceCounter> dict;

      public PhotoFaceCounters () {
         dict = new Dictionary<string, PhotoFaceCounter> (2000);
      }

      private PhotoFaceCounter getCounter(DbFace face) {
         string mainId = face.MainId;
         if (!dict.TryGetValue (mainId, out var counter))
            dict.Add (mainId, counter = new PhotoFaceCounter (mainId));
         return counter;
      }
      public void AddCounters (DbFace face) {
         switch (face.NameSrc) {
            case NameSource.Manual:
               getCounter (face).Manual++;
               break;
            case NameSource.Unknown:
               getCounter (face).Unknown++;
               break;
            case NameSource.Corrected:
               getCounter (face).Corrected++;
               break;
         }
      }

      private static int cbSortId (PhotoFaceCounter a, PhotoFaceCounter b) {
         return string.CompareOrdinal (a.Id, b.Id);
      }

      private static void logMissing (Logger logger, PhotoFaceCounter old, int manual, int unknown, int corrected) {
         logger.Log (_LogType.ltWarning, "Missing defined faces for photo [{0}]: manual={1} (was {2}), error={3} (was {4}), corrected={5} (was {6}).",
                     old.Id, manual, old.Manual, unknown, old.Unknown, corrected, old.Corrected);
      }
      public int LogDifferences (Logger logger, PhotoFaceCounters old) {
         var ourItems = dict.Values.ToList ();
         var oldItems = old.dict.Values.ToList ();
         ourItems.Sort (cbSortId);
         oldItems.Sort (cbSortId);
         int i = 0, j = 0, missing=0;
         while (i < ourItems.Count && j < oldItems.Count) {
            var ourItem = ourItems[i];
            var oldItem = oldItems[j];
            int cmp = string.CompareOrdinal (oldItem.Id, ourItem.Id);
            if (cmp==0) {
               if (ourItem.Manual < oldItem.Manual || ourItem.Unknown < oldItem.Unknown || ourItem.Corrected < oldItem.Corrected) {
                  logMissing (logger, oldItem, ourItem.Manual, ourItem.Unknown, ourItem.Corrected);
                  missing++;
               }
               i++;
               j++;
               continue;
            }
            if (cmp < 0) {
               logMissing (logger, oldItem, 0, 0, 0);
               missing++;
               j++;
               continue;
            }
            i++;
         }

         //Handle rest of oldItems
         missing += oldItems.Count - j;
         for (; j < oldItems.Count; j++) {
            logMissing (logger, oldItems[j], 0, 0, 0);
         }

         if (missing == 0) logger.Log (_LogType.ltInfo, "No missing defined faces!");
         else logger.Log (_LogType.ltWarning, "Photo's with missing defined faces: {0}", missing);
         return missing;
      }

      class PhotoFaceCounter {
         public readonly string Id;
         public int Manual;
         public int Unknown;
         public int Corrected;
         public PhotoFaceCounter (string id) {
            Id = id;
         }
      }
   }
}

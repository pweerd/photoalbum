using Bitmanager.Cache;
using Bitmanager.Core;
using Bitmanager.Elastic;
using Bitmanager.Json;
using Bitmanager.Web;
using System.Text.RegularExpressions;

namespace BMAlbum {

   public class Pin {
      public readonly static Regex locExpr = new Regex (@"^[\d+\-\.]+,[\d+\-\.]+$", RegexOptions.Compiled);
      public readonly string Id;
      public readonly string Position;
      public readonly string Distance;
      public readonly string Album;
      public readonly ESQuery IdQuery;

      private static readonly LRUCache cache = new LRUCache (20);
      private static readonly ESQuery existQuery = new ESExistsQuery ("location");
      private static readonly Logger logger = Logs.CreateLogger ("site", "pin");

      public static Pin Create (string value) {
         if (value == null) return null;
         logger.Log("Create pin for {0}", value);
         string dist = null;
         string locOnly = value;
         int idx = value.LastIndexOf ('~');
         if (idx > 0) {
            dist = value.Substring (idx + 1).TrimToNull ();
            locOnly = value.Substring (0, idx);
         }

         Pin pin = null;
         lock (cache) {
            pin = (Pin)cache.Get(locOnly);
         }
         if (pin != null) goto EXIT_RTN;

         pin = new Pin(locOnly);
         lock (cache) {
            if (pin.Position != null) cache.GetOrAdd (pin.Position, pin);
            if (pin.Id != null) cache.GetOrAdd (pin.Id, pin);
         }

      EXIT_RTN:
         logger.Log("-- found {0}, id={1}", pin.Position, pin.Id);
         return dist == null ? pin : new Pin (pin, dist);
      }

      private Pin(Pin other, string dist) {
         Id = other.Id;
         Position = other.Position;
         Album = other.Album;
         if (dist != null) 
            Distance = dist.IsNumeric () ?  dist+"km" : dist;
      }

      private Pin (string value) {
         var settings = (Settings)WebGlobals.Instance.Settings;
         if (locExpr.IsMatch (value)) {
            Position = value;
         } else {
            Id = value;
            var req = settings.ESClient.CreateSearchRequest (settings.MainIndex);
            req.Size = 1;
            req.SetSource ("location,album", null);
            IdQuery = new ESIdsQuery (null, value);
            req.Query = new ESBoolQuery ().AddFilter (existQuery).AddMust (IdQuery);
            logger.Log ("-- search id in id");

            var resp = req.Search ();
            resp.ThrowIfError ();
            if (resp.Documents.Count == 0) {
               IdQuery = new ESMatchQuery ("file", value).SetOperator(ESQueryOperator.and);
               req.Query = new ESBoolQuery ().AddFilter (existQuery).AddMust (IdQuery);
               logger.Log ("-- search id in file");
               resp = req.Search ();
               resp.ThrowIfError ();
               if (resp.Documents.Count == 0) {
                  logger.Log ("Unknown photo [{0}].", value);
                  return;
               }
               //if (resp.Documents.Count == 0) throw new BMException ("Unknown photo [{0}].", value);
            }
            var src = resp.Documents[0]._Source;
            Position = src.ReadStr ("location", null);
            if (Position == null) {
               logger.Log ("Photo [{0}] without location.", value);
               throw new BMException ("Photo [{0}] without location.", value);
            }
            Album = src.ReadStr ("album", null);
         }
      }

      public string ToUrlValue () {
         string loc = Id ?? Position;
         return Distance == null ? loc : loc + "~" + Distance;
      }

      public override string ToString () {
         return ToUrlValue ();
      }

      public JsonObjectValue ToJson () {
         var ret = new JsonObjectValue ();
         ret.Add ("loc", Position);
         if (Id != null) ret.Add ("id", Id);
         if (Album != null) ret.Add ("album", Album);
         return ret;
      }
      public string ToQuery () {
         return "pin:\"" + ToUrlValue() + "\"";
      }
   }
}

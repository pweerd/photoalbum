using Bitmanager.Core;
using Bitmanager.Elastic;
using Bitmanager.Json;
using Bitmanager.Web;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace BMAlbum {

   public class Pin {
      public readonly static Regex locExpr = new Regex (@"^[\d ,+\-\.]+$", RegexOptions.Compiled);
      public readonly string Id;
      public readonly string Position;
      public readonly string Album;

      public Pin (string id, string position, string album) {
         Id = id;
         Position = position;
         Album = album;
      }
      public Pin (string value) {
         if (locExpr.IsMatch (value)) {
            Position = value;
         } else {
            var settings = (Settings)WebGlobals.Instance.Settings;
            var c = settings.ESClient.CreateSearchRequest (settings.MainIndex);
            c.Size = 1;
            c.SetSource ("location,album", null);
            c.Query = new ESIdsQuery ("_doc", value);
            var resp = c.Search ();
            if (resp.Documents.Count == 0) throw new BMException ("Unknown photo [{0}].", value);
            var src = resp.Documents[0]._Source;
            Position = src.ReadStr ("location", null);
            if (Position==null) throw new BMException ("Photo [{0}] without location.", value);
            Album = src.ReadStr ("album", null);
         }
      }

      public string ToUrlValue () {
         return Id ?? Position;
      }

      public JsonObjectValue ToJson () {
         var ret = new JsonObjectValue ();
         ret.Add ("position", Position);
         if (Id != null) ret.Add ("id", Id);
         if (Album != null) ret.Add ("album", Album);
         return ret;
      }
   }
}

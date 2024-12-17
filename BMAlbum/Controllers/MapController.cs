using Bitmanager.Core;
using Bitmanager.Elastic;
using Bitmanager.Json;
using Bitmanager.Query;
using Bitmanager.Web;
using BMAlbum.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NuGet.Configuration;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace BMAlbum.Controllers {
   public class MapController : BaseController {
      const string FIELD = "location";
      readonly static Regex locExpr = new Regex (@"^[\d ,+\-]+$", RegexOptions.Compiled);
      public string Pin;

      public IActionResult Index () {
         var pin = Request.ReadStr ("pin", null);
         if (pin != null) {
            SiteLog.Log ("Pin1={0}", pin);
            if (!locExpr.IsMatch(pin)) {
               var settings = (Settings)Settings;
               var c = settings.ESClient;
               var req = c.CreateSearchRequest (settings.MainIndex);
               req.Size = 1;
               req.Query = new ESIdsQuery ("_doc", pin);
               req.SetSource (FIELD, null);

               var resp = req.Search ();
               resp.ThrowIfError ();
               if (resp.Documents.Count == 0) pin = null;
               else pin = resp.Documents[0].ReadStr (FIELD, null);
            }
            SiteLog.Log ("Pin2={0}", pin);
            Pin = pin;
         }

         return View (new MapModel (this, new ClientState (RequestCtx, (Settings)Settings)));
      }

      private enum _Mode { clusters, photos};
      public IActionResult Clusters () {
         var mem = new MemoryStream ();
         using (var strm = Request.Body) {
            strm.CopyToAsync (mem).GetAwaiter ().GetResult ();
         }
         mem.Position = 0;

         var zoom = Request.Query.ReadInt ("zoom", -1);
         var bounds = Request.Query.ReadStr ("bounds", null);
         var mode = Request.Query.ReadEnum ("mode", _Mode.clusters);
         var settings = (Settings)Settings;
         var clientState = new ClientState (RequestCtx, settings);
         var debug = (clientState.DebugFlags & DebugFlags.TRUE) != 0;

         SEARCH:
         var c = settings.ESClient;
         var req = c.CreateSearchRequest (settings.MainIndex);
         var albumAgg = new ESTermsAggregation ("albums", "album.facet", 3);
         if (mode==_Mode.clusters) {
            req.Size = 0;
            var agg = new ESGeoHashAggregation ("clusters", FIELD, zoom);
            req.Aggregations.Add (agg);
            agg.SubAggs.Add (albumAgg);
         } else {
            req.Size = 500;
            req.SetSource (FIELD + ",album", null);
            req.Aggregations.Add (albumAgg);
            albumAgg.Size = 10;
         }

         ESQuery q = bounds != null ? new ESGeoBoundingBoxQuery (FIELD, bounds) : new ESExistsQuery (FIELD);
         req.Query = PhotoController.wrapQueryInFilters (clientState, q, null);

         var resp = req.Search ();
         resp.ThrowIfError ();

         //If the #photo's is limied and we are clustering, we redo the search,
         //but now for getting photo's
         if (resp.TotalHits < 40 && mode == _Mode.clusters) {
            mode = _Mode.photos;
            goto SEARCH; 
         }

         var json = new JsonMemoryBuffer ();
         json.WriteStartObject ();
         json.WriteProperty ("zoom", zoom);

         json.WriteStartObject ("clusters");
         if (mode == _Mode.clusters) {
            var aggResult = (ESGeoAggregationResult)resp.Aggregations.FindByName ("clusters", true);
            for (int i = 0; i < aggResult.ItemCount; i++) {
               var item = aggResult.Items[i];
               var k = item.GetKey ();
               json.WriteStartObject (k);
               json.WriteProperty ("loc", hashToLocation (k));
               json.WriteProperty ("count", item.Count);
               var str = fetchAlbumsAsString (item);
               if (str != null) json.WriteProperty ("albums", str);

               json.WriteEndObject ();
            }
         }
         json.WriteEndObject ();

         MapColorDict colorDict = null;
         json.WriteStartObject ("photos");
         if (resp.Documents.Count>0) {
            colorDict = new MapColorDict (settings.MapSettings);
            if (mem.Length>2) {
               var existingColors = JsonObjectValue.Load (mem);
               foreach (var kvp in existingColors) {
                  colorDict.SetColorIndex (kvp.Key, kvp.Value.AsInt ());
               }
            }

            markBestAlbums (colorDict, resp.Aggregations);
            foreach (var d in resp.Documents) {
               var src = d._Source;
               json.WriteStartObject (d.Id);
               json.WriteProperty ("loc", src.ReadStr (FIELD, string.Empty));
               var a = src.ReadStr ("album", string.Empty);
               json.WriteProperty ("album", a);
               json.WriteProperty ("count", 1);
               json.WriteProperty ("color", colorDict.GetColorIndex(a));
               json.WriteEndObject ();
            }
         }
         json.WriteEndObject ();
         if (colorDict != null) colorDict.ExportToJson (json);

         json.WriteEndObject ();
         return new JsonActionResult (json);
      }

      string fetchAlbumsAsString (ESAggregationResultItem agg) {
         var albums = agg.FindInnerAggregation ("albums", false);
         if (albums == null || albums.ItemCount == 0) return null;

         var sb = new StringBuilder ();
         for (int i = 0; i < albums.ItemCount; i++) {
            if (sb.Length > 0) sb.Append ('\n');
            var item = albums.Items[i];
            var k = item.GetKey ();
            sb.Append (item.GetKey ()).Append (' ').Append ('(').Append (item.Count).Append (')');
         }
         return sb.ToString ();
      }

      class AlbumAdmin {
         public readonly string Album;
         public readonly int Count;
         public readonly int Index;
         public AlbumAdmin (ESAggregationResultItem item, int index) {
            Album = item.GetKey ();
            Count = item.Count;
            Index = index; 
         }

      }
      void markBestAlbums (MapColorDict dict, ESAggregationResults agg) {
         var ret = new Dictionary<string,AlbumAdmin> ();
         var albums = agg.FindByName ("albums", false);
         if (albums != null && albums.ItemCount > 0){
            for (int i = 0; i < albums.ItemCount; i++) {
               dict.GetColorIndex (albums.Items[i].GetKey());
            }
         }
      }

      private static string hashToLocation(string hash) {
         var lat = GeoHash.Decode (hash, out var lon);
         return Invariant.Format ("{0},{1}", lat, lon);
      }


      public IActionResult H3Clusters (int geoZoom, string bounds) {
         var settings = (Settings)Settings;
         var clientState = new ClientState (RequestCtx, settings);
         var debug = (clientState.DebugFlags & DebugFlags.TRUE) != 0;

         var c = settings.ESClient;
         var req = c.CreateSearchRequest (settings.MainIndex);
         req.Size = 0;

         var agg = new ESGeoH3Aggregation ("clusters", FIELD, geoZoom);
         req.Aggregations.Add (agg);

         ESQuery q = bounds != null ? new ESGeoBoundingBoxQuery (FIELD, bounds) : new ESExistsQuery (FIELD);
         req.Query = PhotoController.wrapQueryInFilters (clientState, q, null);

         var resp = req.Search ();
         resp.ThrowIfError ();

         var aggResult = (ESGeoAggregationResult)resp.Aggregations.FindByName ("clusters", true);
         var json = new JsonMemoryBuffer ();
         json.WriteStartObject ();
         json.WriteProperty ("zoom", geoZoom);
         json.WriteStartArray ("clusters");

         for (int i = 0; i < aggResult.ItemCount; i++) {
            json.WriteStartObject ();
            var item = aggResult.Items[i];
            json.WriteProperty ("key", item.GetKey ());
            json.WriteProperty ("count", item.Count);
            json.WriteEndObject ();

         }
         json.WriteEndArray ();
         json.WriteEndObject ();

         return new JsonActionResult (json);
      }

      public IActionResult Dump (string pos) {
         var settings = (Settings)Settings;
         var clientState = new ClientState (RequestCtx, settings);
         if (!clientState.InternalIp) return new ActionResult404 (); //only for internal use
         var debug = (clientState.DebugFlags & DebugFlags.TRUE) != 0;

         var c = settings.ESClient;
         var req = c.CreateSearchRequest (settings.MainIndex);
         req.Size = 0;

         var geoQ = new ESGeoDistanceQuery (FIELD, pos, "20km");
         var fsq = new ESFunctionScoreQuery (geoQ);
         fsq.BoostMode = ESBoostMode.replace;

         var func = new ESScoreFunctionDecay (ESScoreFunctionDecay.DecayFunctionType.linear, FIELD);
         func.Scale = "20km";
         func.Origin = pos; ;
         func.Decay = 0.1;
         fsq.Functions.Add (func);
         req.Query = fsq;

         req.Size = 10;
         var resp = req.Search ();
         resp.ThrowIfError ();

         var json = new JsonMemoryBuffer ();
         json.WriteStartObject ();
         json.WriteStartArray ("docs");

         SiteLog.Log ("Dumping {0} results for pos={1}", resp.Documents.Count, pos);
         foreach (var d in resp.Documents) {
            var a = d.ReadStr ("album");
            SiteLog.Log ("-- {0}, {1}, {2}", d.Score, a, d.Id);
            json.WriteStartObject ();
            json.WriteProperty ("score", d.Score);
            json.WriteProperty ("id", d.Id);
            json.WriteProperty ("album", a);
            json.WriteEndObject ();
         }

         json.WriteEndArray ();
         json.WriteEndObject ();

         return new JsonActionResult (json);

      }
   }
}

using Bitmanager.Core;
using Bitmanager.Elastic;
using Bitmanager.Json;
using Bitmanager.Query;
using Bitmanager.Web;
using BMAlbum.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NuGet.Configuration;
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
         var zoom = Request.Query.ReadInt ("zoom", -1);
         var bounds = Request.Query.ReadStr ("bounds", null);
         var mode = Request.Query.ReadEnum ("mode", _Mode.clusters);
         var settings = (Settings)Settings;
         var clientState = new ClientState (RequestCtx, settings);
         var debug = (clientState.DebugFlags & DebugFlags.TRUE) != 0;
         ESQuery notFilter;
         if (RequestCtx.IsInternalIp)
            notFilter = clientState.Unhide ? null : PhotoController.hideExternal;
         else {
            clientState.Unhide = false;
            notFilter = PhotoController.hideAll;
         }

         var c = settings.ESClient;
         var req = c.CreateSearchRequest (settings.MainIndex);
         if (mode==_Mode.clusters) {
            req.Size = 0;
            var agg = new ESGeoHashAggregation ("clusters", FIELD, zoom);
            req.Aggregations.Add (agg);
         } else {
            req.Size = 100;
            req.SetSource (FIELD + ",album", null);
         }
         var bq = new ESBoolQuery ();
         bq.AddFilter (clientState.User.Filter);
         bq.AddNot (notFilter);
         bq.AddFilter(bounds != null ? new ESGeoBoundingBoxQuery (FIELD, bounds) 
                                     : new ESExistsQuery (FIELD));
         req.Query = bq;

         var resp = req.Search ();
         resp.ThrowIfError ();

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
               json.WriteEndObject ();
            }
         }
         json.WriteEndObject ();

         json.WriteStartObject ("photos");
         foreach (var d in resp.Documents) {
            var src = d._Source;
            json.WriteStartObject (d.Id);
            json.WriteProperty ("loc", src.ReadStr (FIELD, string.Empty));
            json.WriteProperty ("album", src.ReadStr ("album", string.Empty));
            json.WriteProperty ("count", 1);
            json.WriteEndObject ();
         }
         json.WriteEndObject ();

         json.WriteEndObject ();
         return new JsonActionResult (json);
      }

      private static string hashToLocation(string hash) {
         var lat = GeoHash.Decode (hash, out var lon);
         return Invariant.Format ("{0},{1}", lat, lon);
      }


      public IActionResult H3Clusters (int geoZoom, string bounds) {
         var settings = (Settings)Settings;
         var clientState = new ClientState (RequestCtx, settings);
         var debug = (clientState.DebugFlags & DebugFlags.TRUE) != 0;
         ESQuery notFilter;
         if (RequestCtx.IsInternalIp)
            notFilter = clientState.Unhide ? null : PhotoController.hideExternal;
         else {
            clientState.Unhide = false;
            notFilter = PhotoController.hideAll;
         }

         var c = settings.ESClient;
         var req = c.CreateSearchRequest (settings.MainIndex);
         req.Size = 0;

         var agg = new ESGeoH3Aggregation ("clusters", FIELD, geoZoom);
         req.Aggregations.Add (agg);
         var bq = new ESBoolQuery ();
         bq.AddFilter (clientState.User.Filter);
         bq.AddNot (notFilter);
         bq.AddFilter (bounds != null ? new ESGeoBoundingBoxQuery (FIELD, bounds)
                                     : new ESExistsQuery (FIELD));
         req.Query = bq;

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
         var debug = (clientState.DebugFlags & DebugFlags.TRUE) != 0;
         ESQuery notFilter;
         if (RequestCtx.IsInternalIp)
            notFilter = clientState.Unhide ? null : PhotoController.hideExternal;
         else {
            clientState.Unhide = false;
            notFilter = PhotoController.hideAll;
         }

         var c = settings.ESClient;
         var req = c.CreateSearchRequest (settings.MainIndex);
         req.Size = 0;

         var bq = new ESBoolQuery ();
         //PWbq.AddFilter (clientState.User.Filter);
         bq.AddNot (notFilter);

         var geoQ = new ESGeoDistanceQuery (FIELD, pos, "20km");
         var fsq = new ESFunctionScoreQuery (geoQ);
         fsq.BoostMode = ESBoostMode.replace;

         var func = new ESScoreFunctionDecay (ESScoreFunctionDecay.DecayFunctionType.linear, FIELD);
         func.Scale = "20km";
         func.Origin = pos; ;
         func.Decay = 0.1;
         fsq.Functions.Add (func);
         bq.AddMust (fsq);

         req.Query = bq;

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


      private static int googleZoomToGeoHashZoom(int zoom) {
         return (int)Math.Round(1 + 11 * (zoom / 23.0)); 
      }
   }
}

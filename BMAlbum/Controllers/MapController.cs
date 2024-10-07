using Bitmanager.Elastic;
using Bitmanager.Json;
using Bitmanager.Query;
using Bitmanager.Web;
using BMAlbum.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NuGet.Configuration;

namespace BMAlbum.Controllers {
   public class MapController : BaseController {
      private static readonly int[] googleZoomToEsZoom = {
         1,  //0
         1,  //1
         1,  //2
         2,  //3
         2,  //4
         3,  //5
         3,  //6
         1,  //7
         4,  //8
         4,  //9
         5,  //10
         5,  //11
         6,  //12
         6,  //13
         6,  //14
         7,  //15
         7,  //16
         7,  //17
         8,  //18
         8,  //19
         8,  //20
         9,  //21
         9   //22
      };
      const string FIELD = "location";
      public IActionResult Index () {
         return View (new MapModel (this, new ClientState (RequestCtx, (Settings)Settings)));
      }

      public IActionResult Clusters (int zoom, string bounds) {
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
         var geoHashZoom = googleZoomToEsZoom[zoom];

         var agg = new ESGeoHashAggregation ("clusters", FIELD, geoHashZoom);
         req.Aggregations.Add (agg);
         var bq = new ESBoolQuery ();
         bq.AddFilter (clientState.User.Filter);
         bq.AddNot (notFilter);
         bq.AddFilter(bounds != null ? new ESGeoBoundingBoxQuery (FIELD, bounds) 
                                     : new ESExistsQuery (FIELD));
         req.Query = bq;

         var resp = req.Search ();
         resp.ThrowIfError ();

         var aggResult = (ESGeoHashAggregationResult)resp.Aggregations.FindByName ("clusters", true);
         var json = new JsonMemoryBuffer ();
         json.WriteStartObject ();
         json.WriteProperty ("zoom", geoHashZoom);
         json.WriteStartArray ("clusters");

         for (int i = 0; i < aggResult.ItemCount; i++) {
            json.WriteStartObject ();
            var item = aggResult.Items[i];
            json.WriteProperty ("loc", item.GetKey ());
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

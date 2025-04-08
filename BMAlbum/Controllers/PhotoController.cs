/*
 * Copyright © 2024, De Bitmanager
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

using Bitmanager.Json;
using Bitmanager.Web;
using BMAlbum.Models;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.Text;
using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using Bitmanager.Imaging;
using Bitmanager.Core;
using Bitmanager.IO;
using Bitmanager.Xml;
using Bitmanager.Web.ActionResults;
using Bitmanager.Elastic;
using Bitmanager.BoolParser;
using Image = System.Drawing.Image;
using Bitmanager.Query;
using System.Text.RegularExpressions;
using static System.Net.WebRequestMethods;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.AspNetCore.Http.Extensions;

namespace BMAlbum.Controllers {


   public class PhotoController : BaseController {
      internal static readonly ESQuery hideAll = new ESTermsQuery ("hide", "always", "external");
      internal static readonly ESQuery hideSuperhidden = new ESTermQuery ("hide", "always");
      private PhotoCache cache;
      private Settings settings;

      internal void setSettings (Settings settings) {
         this.settings = settings;
         this.cache = settings.PhotoCache;
      }

      protected override void init () {
         base.init ();
         settings = (Settings)base.Settings;
         cache = settings.PhotoCache;
      }

      private static string facetToField (string facet) {
         return facet == "album" ? "album.facet" : facet;
      }

      public static ESQuery wrapQueryInFilters (ClientState state, ESQuery q, ESBoolQuery restFilter) {
         if (q == null)
            q = restFilter;
         else if (restFilter != null) {
            restFilter.AddMust (q);
            q = restFilter;
         }

         ESQuery notFilter = null;
         if (state.InternalIp)
            notFilter = state.Unhide ? null : hideSuperhidden;
         else
            notFilter = hideAll;

         q = ESBoolQuery.CreateNotFilteredQuery (q, notFilter);
         return ESBoolQuery.CreateFilteredQuery (q, state.User.Filter);
      }


      public IActionResult Index () {
         var clientState = new ClientState (RequestCtx, settings);
         SiteLog.Log ("Index: q={0}, pin={1}, perAlbum={2}", clientState.Query, clientState.Pin, clientState.PerAlbum);
         var searchSettings = settings.MainSearchSettings;
         var reqSortMode = clientState.Sort ?? searchSettings.SortModes.Default;
         if (string.Equals ("auto", reqSortMode.Name, StringComparison.OrdinalIgnoreCase))
            reqSortMode = null;
         var curSortMode = reqSortMode;

         var timings = new List<ESTimerStats> ();
         int SIZE = clientState.ActualPageSize;
         var debug = (clientState.DebugFlags & DebugFlags.TRUE) != 0;

         switch (BMAlbum.User.CheckAccess (clientState.User, RequestCtx.RemoteIPClass, isAuthenticated ())) {
            case _Access.Ok: break;
            default: return new JsonActionResult ();
         }

         var lbSettings = settings.LightboxSettings;
         var c = settings.ESClient;
         var req = c.CreateSearchRequest (settings.MainIndex);
         req.TrackTotalHits = true;
         req.Size = 0;

         var knownFacets = new Dictionary<string, ESAggregation> ();

         string query = clientState.Query;
         if (query == null && clientState.Pin != null) 
            query = clientState.Pin.ToQuery ();
         var agg = new ESTermsAggregation ("album", "album.facet");
         agg.Sort.Add ("sort_key", false);
         agg.Size = SIZE;
         agg.MinDocCount = lbSettings.MinCountForAlbum;

         //PW weg
         ////If a direct show is requested, we don't want a min album count, since it could be the only photo.
         //if (clientState.Slide == null) 
         //   agg.MinDocCount = lbSettings.MinCountForAlbum;
         //else
         //   if (clientState.PerAlbum == TriStateBool.Unspecified) 
         //      clientState.PerAlbum = TriStateBool.True;


         var subJson = new JsonObjectValue ("field", "sort_key");
         subJson = new JsonObjectValue ("max", subJson);
         agg.SubAggs.Add (new ESJsonAggregation ("sort_key", subJson));
         knownFacets.Add ("album", agg);

         agg = new ESTermsAggregation ("year", "year");
         agg.Sort.Add (ESAggregation.AggSortMethod.Desc | ESAggregation.AggSortMethod.Item);
         agg.Size = SIZE;
         knownFacets.Add ("year", agg);

         ESBoolQuery restFilter = null;
         var facets = clientState.Facets;
         for (int i = facets.Count - 1; i >= 0; i--) {
            var filter = new ESTermQuery (facetToField (facets[i].Key), facets[i].Value);
            if (req.PostFilter == null) {
               req.PostFilter = filter;
               continue;
            }
            if (restFilter == null) restFilter = new ESBoolQuery ();
            restFilter.AddFilter (filter);
         }

         ESAggregations outerAggs = new ESAggregations ();
         var filteredAggs = outerAggs;
         if (req.PostFilter != null) {
            var filterAgg = new ESFilterAggregation ("!", req.PostFilter);
            filteredAggs.Add (filterAgg);
            filteredAggs = filterAgg.SubAggs;
         }

         foreach (var kvp in knownFacets) {
            string f = kvp.Key;
            if (f == clientState.LastFacet)
               outerAggs.Add (kvp.Value);
            else
               filteredAggs.Add (kvp.Value);
         }

         FuzzyMode[] fuzzyModesAll = new FuzzyMode[] {
            FuzzyMode.None,
            FuzzyMode.UnphraseTerms,
            FuzzyMode.UnphraseTerms | FuzzyMode.FuzzyMatch,
            FuzzyMode.UnphraseTerms | FuzzyMode.FuzzyMatch | FuzzyMode.UnphraseFuzzy
         };
         FuzzyMode[] fuzzyModesNoPhrase = new FuzzyMode[] {
            FuzzyMode.None,
            FuzzyMode.FuzzyMatch,
         };

         //If we have a query, lets use the query that results in more than fuzzySettings.TriggerAt hits.
         QueryGenerator queryGenerator = null;
         ESSearchResponse resp = null;
         var json = new JsonMemoryBuffer ();
         json.WriteStartObject ();

         List<ParserValueNode> valueNodes = null;
         bool hasFuzzy = false;
         if (query != null) {

            queryGenerator = new QueryGenerator (searchSettings, settings.IndexInfoCache.GetIndexInfo(settings.MainIndex), query);
            if (queryGenerator.ParseResult.Root is ParserEmptyNode) queryGenerator = null;
            else {
               int phraseNodes = 0;
               //int normalNodes = 0;
               //int fuzzyEligiblePhraseNodes = 0;
               int fuzzyEligibleNormalNodes = 0;

               valueNodes = queryGenerator.ParseResult.Root.CollectValueNodes ();
               foreach (var node in valueNodes) {
                  if (node is ParserPhraseValueNode) {
                     ++phraseNodes;
                     continue;
                  }
                  if (node.GetType() == typeof (ParserValueNode)) {
                     if (node.Value.Length > 2) ++fuzzyEligibleNormalNodes;
                     continue;
                  }
               }
               FuzzyMode[] fuzzyModes = phraseNodes > 0 ? fuzzyModesAll : fuzzyModesNoPhrase;

               var fuzzySettings = searchSettings.FuzzySettings;
               ESQuery exactQ = queryGenerator.GenerateQuery (FuzzyMode.None);
               ESQuery q = exactQ;
               ESQuery prevQ = q;
               int i = 0;
               int prevCount = 0;
               ESCountResponse countResp;
               while (true) {
                  req.Query = wrapQueryInFilters (clientState, q, restFilter);
                  countResp = req.Count ();
                  countResp.ThrowIfError ();
                  if (debug) timings.Add (new ESTimerStats ("count", countResp));
                  if (countResp.Count > prevCount) {
                     prevCount = countResp.Count;
                     if ((fuzzyModes[i] & (FuzzyMode.UnphraseFuzzy | FuzzyMode.FuzzyMatch)) != 0) {
                        hasFuzzy = true;
                     }
                  }
                  if (countResp.Count > fuzzySettings.TriggerAt) break;

                  while (true) {
                     if (++i >= fuzzyModes.Length) goto FINISHED_COUNTING;
                     q = queryGenerator.GenerateQuery (fuzzyModes[i]);
                     if (!sameQuery (q, prevQ)) break;
                  }
                  var dmq = new ESDismaxQuery ();
                  dmq.Add (exactQ);
                  dmq.Add (q.SetBoost(fuzzySettings.Weight));
                  q = dmq;
               }
               FINISHED_COUNTING:
               //if (clientState.PerAlbum == TriStateBool.Unspecified && countResp.Count < SIZE) clientState.PerAlbum = TriStateBool.False;
               if (clientState.PerAlbum == TriStateBool.Unspecified) clientState.PerAlbum = TriStateBool.False;

            }
         }
         if (clientState.PerAlbum == TriStateBool.Unspecified) clientState.PerAlbum = TriStateBool.True;

         bool hasAlbumFacet = clientState.ContainsFacetRequest ("album");
         bool oneQueryIsEnough = (hasAlbumFacet || clientState.PerAlbum == TriStateBool.False || hasFuzzy);
         req.Aggregations = outerAggs;

         if (reqSortMode == null)
            curSortMode = settings.MainAutoSortResolver.ResolveSortMode (valueNodes, hasAlbumFacet, hasFuzzy);
         SiteLog.Log ("AFTER count: hasFuzzy={0}, sortmode={1}", hasFuzzy, curSortMode);

         curSortMode.ToSearchRequest (req);
         if (debug) optSetExplain (req);
         if (oneQueryIsEnough) {
            req.Size = SIZE;
         } else {
            req.Size = 1;
            req.SetSource ("album", null); //Fetch 1 doc with album only
         }

         if (req.Query == null) req.Query = wrapQueryInFilters (clientState, null, restFilter);
         resp = req.Search ();
         resp.ThrowIfError ();
         List<GenericDocument> docs = resp.Documents;

         if (debug) timings.Add (new ESTimerStats ("search", resp));
         SiteLog.Log ("HasAlbum={0}, PerAlbum={1}, oneQueryIsEnough={2}", hasAlbumFacet, clientState.PerAlbum, oneQueryIsEnough);
         SiteLog.Log ("Query [{0}] has {1} total results.", query, resp.TotalHits);

         int firstAlbumCount;
         string firstAlbum = writeAlbums (json, findResult (resp.Aggregations, "album"), out firstAlbumCount);
         writeYears (json, findResult (resp.Aggregations, "year"));
         if (oneQueryIsEnough) goto WRITE_RESPONSE;

         //We should do a per-album result
         req.Size = SIZE;
         req.ClearAggregations ();
         req.SetSource (null, null);  //Reset source: request all fields


         //If a per-album result with an album, just do the original query with the correct size
         if (docs.Count>0 && (firstAlbum==null || hasLocationQuery(valueNodes))) { 
            var album0 = docs[0].ReadStr ("album", null);
            SiteLog.Log("Replacing album if nonNull: was {0}, into {1}", firstAlbum, album0);
            if (album0 != null) firstAlbum = album0;
         }
         if (firstAlbum == null) {
            resp = req.Search ();
            resp.ThrowIfError ();
            if (debug) timings.Add (new ESTimerStats ("search", resp));
            goto WRITE_RESPONSE;
         }

         //Do the per-album query
         json.WriteProperty ("cur_album", firstAlbum);
         var albumQuery = new ESTermQuery ("album.facet", firstAlbum);
         req.Query = wrapQueryInFilters (clientState, albumQuery, null);
         if (reqSortMode == null)
            curSortMode = settings.MainAutoSortResolver.ResolveSortMode (valueNodes, true, hasFuzzy);
         curSortMode.ToSearchRequest (req);
         if (debug) optSetExplain (req);

         resp = req.Search ();
         resp.ThrowIfError ();

         if (debug) timings.Add (new ESTimerStats ("search", resp));

         WRITE_RESPONSE:
         SiteLog.Log ("Final sortmethod: {0}", curSortMode);
         docs = resp.Documents;
         if ((clientState.DebugFlags & DebugFlags.ONE) != 0 && docs.Count>1)
            docs.RemoveRange (1, docs.Count - 1);
         json.WriteProperty ("new_state", (IJsonSerializable)clientState.ToJson ());

         writeFiles (json, docs, debug, queryGenerator);

         if (debug) {
            json.WriteStartObject ("dbg");
            json.WriteStartArray ("timings");
            foreach (var t in timings) json.WriteValue (t);
            json.WriteEndArray ();
            json.WritePropertyName ("es_request");
            json.WriteValue (req);
            json.WriteEndObject ();
         }

         json.WriteEndObject ();

         return new JsonActionResult (json);
      }


      private static bool sameQuery(ESQuery a, ESQuery b) {
         return (a==null) ? a==b : a.Equals(b); 
      }

      private static bool isSortedOnScore(ESSearchRequest req) {
         if (req.Sort?.Count == 0) return true;
         var first = req.Sort[0];
         return (first.Field == "_score" && first.Mode == ESSortDirection.desc);
      }

      private static void optSetExplain (ESSearchRequest req) {
         var list = req.Sort;
         if (list == null || list.Count == 0) goto SET_EXPLAIN;
         if (list[0].Field == "_score") goto SET_EXPLAIN;
         goto EXIT_RTN;

      SET_EXPLAIN:
         req.Explain = true;

      EXIT_RTN:
         return;
      }

      private static bool hasRelevancyField (List<ParserValueNode> nodes) {
         if (nodes == null) return false;
         for (int i = 0; i < nodes.Count; i++) {
            var processed = nodes[i].ProcessedBy;
            if (processed is LocationSearcher || processed is NameSearcher || processed is PinSearcher)
               return true;
         }
         return false;
      }

      private static bool hasLocationQuery (List<ParserValueNode> nodes) {
         if (nodes == null) return false;
         for (int i = 0; i < nodes.Count; i++) {
            var processed = nodes[i].ProcessedBy;
            if (processed is PinSearcher || processed is LocationSearcher) return true;
         }
         return false;
      }

      private static ESTermsAggregationResult findResult (ESAggregationResults aggs, string name) {
         var agg = aggs.FindByName (name, false);
         if (agg == null) {
            agg = aggs.FindByName ("!", true);
            agg = agg.Aggs.FindByName (name, true);
         }
         return (ESTermsAggregationResult)agg;
      }

      static readonly Regex termExtracter = new Regex (@"weight\(([^:]+):(.+?) in \d+\)|extra_location\:([^\)]+)\)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
      private static void extractExplainTerms (HashSet<string> globalTerms, HashSet<string> terms, JsonObjectValue v) {
         string desc = v.ReadStr ("description", null);
         if (desc != null) {
            var match = termExtracter.Match (desc);
            if (match.Success) {
               string term = match.Groups[2].Value;
               if (string.IsNullOrEmpty (term)) term = match.Groups[3].Value;
               terms.Add (term);
               if (globalTerms != null) globalTerms.Add (term);
               return;
            }
         }
         var arr = v.ReadArr ("details", null);
         if (arr != null) {
            for (int i = 0; i < arr.Count; i++) {
               extractExplainTerms (globalTerms, terms, (JsonObjectValue)arr[i]);
            }
         }
      }

      private void writeFiles (JsonBuffer json, List<GenericDocument> docs, bool dbg, QueryGenerator queryGenerator) {
         HashSet<string> gTerms = new HashSet<string> ();
         HashSet<string> terms = new HashSet<string> ();
         var transTerms = queryGenerator?.Translated;
         if (transTerms != null) foreach (var tq in transTerms) gTerms.Add (tq.ToString ());
         double maxRatio = 1;
         double maxWRatio = 1;
         double minWRatio = 100;
         double meanWRatio = 0;
         json.WriteStartArray ("files");
         foreach (var doc in docs) {
            int w = doc.ReadInt ("width");
            int h = doc.ReadInt ("height");
            switch (doc.ReadInt ("orientation", 0)) {
               case (int)ExifOrientation.Rotate_90:
               case (int)ExifOrientation.Rotate_270:
                  int tmp = w;
                  w = h;
                  h = tmp;
                  break;
            }
            json.WriteStartObject ();
            json.WriteProperty ("f", doc.Id);
            json.WriteProperty ("a", doc.ReadStr ("album", null));
            json.WriteProperty ("y", doc.ReadStr ("year", null));
            json.WriteProperty ("w", w);
            json.WriteProperty ("h", h);
            json.WriteProperty ("t_nl", doc.ReadStr ("text_nl", null));
            json.WriteProperty ("t_ocr", doc.ReadStr ("ocr", null));
            int faces = doc.ReadInt ("face_count", 0);
            if (faces>0) json.WriteProperty ("fcnt", faces);

            if (dbg) {
               json.WriteProperty ("t_en", doc.ReadStr ("text_en", null));
               json.WriteProperty ("t_txt", doc.ReadStr ("text", null));
               json.WriteProperty ("score", doc.Score);
               var explain = doc.FormattedExplain;
               if (explain != null) json.WriteProperty ("explain", (IJsonSerializable)explain);
            }
            var names = doc.ReadArr ("names", null);
            if (names != null) json.WriteProperty ("names", (IJsonSerializable)names);
            var dt = doc.ReadDate ("date", DateTime.MinValue);
            if (dt != DateTime.MinValue) {
               json.WriteProperty ("date", dt.ToLocalTime ().ToString ("yyyy-MM-dd HH:mm:ss"));
               json.WriteProperty ("year", doc.ReadInt ("year"));
            }
            var str = doc.ReadStr ("camera", null);
            if (str != null) json.WriteProperty ("c", str);
            str = doc.ReadStr ("tz", null);
            if (str != null) json.WriteProperty ("tz", str);
            str = doc.ReadStr ("trkid", null);
            if (str != null) json.WriteProperty ("trkid", str);
            str = doc.ReadStr ("location", null);
            if (str != null) json.WriteProperty ("l", str);

            double ratio = w;
            if (w<=h) {
               ratio = h / ratio;
               if (ratio > 2) ratio = 2;
               if (ratio > maxRatio) maxRatio = ratio;
            } else {
               ratio = ratio / h;
               if (ratio > 2) ratio = 2;
               if (ratio > maxRatio) maxRatio = ratio;
               if (ratio > maxWRatio) maxWRatio = ratio;
               if (ratio < minWRatio) minWRatio = ratio;
               meanWRatio += ratio;
            }
            json.WriteProperty ("r", ratio);
            if (dbg) json.WriteProperty ("sk", doc.ReadStr ("sort_key"));

            if (doc.Explain != null) {
               terms.Clear ();
               extractExplainTerms (gTerms, terms, doc.Explain);
               json.WriteProperty ("terms", string.Join (";", terms));
            }

            json.WriteEndObject ();
         }
         json.WriteEndArray ();
         json.WriteProperty ("max_ratio", Math.Round (maxRatio, 4));
         json.WriteProperty ("max_w_ratio", Math.Round (maxWRatio, 4));
         json.WriteProperty ("mean_w_ratio", Math.Round (docs.Count == 0 ? 1 : meanWRatio / docs.Count, 4));
         json.WriteProperty ("min_w_ratio", Math.Round (minWRatio ==100 ? 1 : minWRatio, 4));
         if (gTerms.Count > 0) json.WriteProperty ("all_terms", string.Join ("\n\t", gTerms));

      }

      private string writeAlbums (JsonBuffer json, ESTermsAggregationResult agg, out int firstCount) {
         string first = null;
         firstCount = 0;

         json.WriteStartArray ("albums");
         var aggItems = agg.Items;
         AlbumWithoutDateComparer.INSTANCE.SortAlbumsWithoutDate (aggItems);
         for (int i=0; i< aggItems.Count; i++) {
            var item = aggItems[i];
            string album = item.GetKey ();
            if (i==0) {
               first = album;
               firstCount = item.Count;
            }
            json.WriteStartObject ();
            json.WriteProperty ("v", album);
            json.WriteEndObject ();
         }
         json.WriteEndArray ();
         return first;
      }


      private void writeYears (JsonBuffer json, ESTermsAggregationResult agg) {
         json.WriteStartArray ("years");
         foreach (var item in agg.Items) {
            json.WriteStartObject ();
            json.WriteProperty ("v", item.GetKey ());
            json.WriteEndObject ();
         }
         json.WriteEndArray ();
      }


      private static string createSmallCacheName (string id, int h) {
         return new StringBuilder ()
            .Append (id)
            .Append ("&h=").Append (h)
            .ToString ();
      }
      private static string createLargeCacheName (string id, int f) {
         return new StringBuilder ()
            .Append (id)
            .Append ("&f=").Append (f)
            .ToString ();
      }
      private IActionResult getSmallImage (string id, int h, string orgFn) {
         var shrinker = settings.ShrinkerSmall;
         h = 240;
         string fn = createSmallCacheName (id, h);
         Stream strm = shrinker.UseCache ? cache.Get (fn, CacheType.Small) : null;
         if (strm == null) {
            Bitmap bm = null; 
            var fs = IOUtils.CreateInputStream (orgFn);
            try {
               bm = (Bitmap)Image.FromStream (fs, false, false);
               shrinker.Shrink (ref bm,  - 1, h);
               strm = saveInCacheAndGet (bm, fn, shrinker.Quality, shrinker.UseCache ? CacheType.Small : CacheType.None);
            } finally {
               bm?.Dispose ();
               fs.Dispose ();
            }
         }
         var ret = new StreamActionResult (strm,
                                           null,
                                           Path.GetFileName (orgFn),
                                           DateTime.UtcNow,
                                           FileDisposition.Attachment);
         return ret.SetCompress (false).SetCache (CacheOptions.Private, TimeSpan.FromDays (7));
      }

      static int _id;

      //private void dumpHeaders() {
      //   var sb = new StringBuilder ();
      //   sb.Append ("Dump headers:");
      //   foreach (var kvp in Request.Headers) {
      //      sb.Append('\n').Append (kvp.Key).Append(": ");
      //      sb.Append (string.Join ("; ", kvp.Value));
      //   }
      //   SiteLog.Log (sb.ToString ());
      //}
      /// <summary>
      /// Negative values for w or h indicate a log 1.5 factor
      /// </summary>
      private IActionResult getLargeImage (string id, int w, int h, string orgFn) {
         //dumpHeaders ();
         if (w <= 0 || h <= 0) throw new BMException ("Unexpected target-size {0}x{1}", w, h);
         string logId = null;
         var shrinker = settings.ShrinkerLarge;
         var logger = shrinker.Logger;
         if (logger != null) {
            logId = "Large" + _id;
            _id++;
            logger.Log (_LogType.ltTimerStart, "{0} starting {1} {2}x{3}", logId, id, w, h);
         }
         ActionResultBase ret = null;

         var fs = IOUtils.CreateInputStream (orgFn);
         Bitmap bm = null;
         try {
            bm = (Bitmap)Image.FromStream (fs, false, false);
            int fp = shrinker.GetFingerPrint(bm, w, h);
            if (fp==0) {
               ret = new FileActionResult (orgFn, FileDisposition.None);
               goto EXIT_RTN;
            }

            string cacheName = createLargeCacheName (id, fp);
            Stream strm = shrinker.UseCache ? cache.Get (cacheName, CacheType.Large) : null;
            if (strm == null) {
               shrinker.Shrink (ref bm, w, h);
               strm = saveInCacheAndGet (bm, cacheName, shrinker.Quality, shrinker.UseCache ? CacheType.Large : CacheType.None);
            }
            ret = new StreamActionResult (strm,
                                          null,
                                          Path.GetFileName (orgFn),
                                          System.IO.File.GetLastWriteTimeUtc(orgFn),
                                          FileDisposition.None);
         } finally {
            bm?.Dispose ();
            fs.Dispose ();
         }

      EXIT_RTN:
         logger?.Log (_LogType.ltTimerStop, "{0} done {1} {2}x{3}", logId, id, w, h);
         return ret
            .SetCompress (false)
            .SetCache (CacheOptions.Private, TimeSpan.FromDays (7))
            ;
      }

      public IActionResult Get (string id) {
         var parms = Request.Query;
         int w = parms.ReadInt ("w", -1);
         int h = parms.ReadInt ("h", -1);
         int dim = parms.ReadInt ("dim", -1);
         int mindim = parms.ReadInt ("mindim", -1);
         if (dim > 0) {
            h = dim;
            w = dim;
         }

         string orgFn = ((Settings)base.Settings).Roots.GetRealFileName (id);
         //Logs.DebugLog.Log ("Requesting img {0}", orgFn);
         if (w <= 0 && h <= 0 && mindim <=0) {
            return PhysicalFile (orgFn, WebGlobals._GetMimeTypeForFile (orgFn));
         }

         if (w<0) {
            return getSmallImage (id, h, orgFn);
         }
         return getLargeImage (id, w, h, orgFn);
      }

      private Stream saveInCacheAndGet (Bitmap bm, String name, int q, CacheType cacheType) {
         var mem = new MemoryStream ();
         ImageUtils.SaveAsJPG (bm, mem, q);
         mem.Position = 0;
         if (cacheType != CacheType.None) {
            cache.Set (name, mem, cacheType);
            mem.Position = 0;
         }
         return mem;
      }

      /// <summary>
      /// Shrinks into the cache
      /// Returns true if the item was already in the cache
      /// </summary>
      public bool ShrinkToCache(CacheType type, string id, int heightOrFactor) {
         if (type == CacheType.Large) return true; //PW moet weg
         string orgFn = this.settings.Roots.GetRealFileName (id);
         string fn = type == CacheType.Large ? createLargeCacheName (id, heightOrFactor)
                                             : createSmallCacheName (id, heightOrFactor);

         bool exists = cache.Exists (fn, type);
         if (!exists) {
            if (System.IO.File.Exists(orgFn)) { 
               IDisposable res = (type==CacheType.Large ? getLargeImage (id, heightOrFactor, heightOrFactor, orgFn)
                                                        : getSmallImage (id, heightOrFactor, orgFn)) as IDisposable;
               res?.Dispose ();
            }
         }
         return exists;
      }

      private class AlbumWithoutDateComparer : IComparer<ESAggregationResultItem> {
         public readonly static AlbumWithoutDateComparer INSTANCE = new AlbumWithoutDateComparer ();

         private AlbumWithoutDateComparer () {}

         public int Compare (ESAggregationResultItem x, ESAggregationResultItem y) {
            return string.CompareOrdinal (x.GetKey (), y.GetKey ());
         }

         public void SortAlbumsWithoutDate (List<ESAggregationResultItem> items) {
            int i = items.Count;
            while (--i > 0) {
               if (!items[i].GetKey ().StartsWith ("[0]")) break;
            }

            //i contains index of last album WITH date
            ++i;
            int N = items.Count - i;
            if (N > 1)
               items.Sort (i, N, this);
         }
      }

   }

}
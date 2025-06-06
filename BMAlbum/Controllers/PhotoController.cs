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
using Bitmanager.Core;
using Bitmanager.IO;
using Bitmanager.Xml;
using Bitmanager.Web.ActionResults;
using Bitmanager.Elastic;
using Bitmanager.BoolParser;
using Bitmanager.Query;
using System.Text.RegularExpressions;
using Bitmanager.ImageTools;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using BMAlbum.Core;

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
            if (!(queryGenerator.ParseResult.Root is ParserEmptyNode)) { 
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

      private static bool isSupportedHtmlVideo(string mime) {
         if (mime == null) return false;
         return mime == "video/mp4" || mime == "video/webm" || mime == "video/ogg";
      }
      private void writeFiles (JsonBuffer json, List<GenericDocument> docs, bool dbg, QueryGenerator queryGenerator) {
         var terms = new HashSet<string> ();
         var gTerms = new HashSet<string> ();
         string str;
         if (queryGenerator != null) {
            var transTerms = queryGenerator?.Translated;
            if (transTerms != null && transTerms.Count > 0) {
               foreach (var tq in transTerms) gTerms.Add (tq.ToString ());
            }
            if (queryGenerator.ParseResult.Errors) json.WriteProperty ("query_error", true);
         }
         double maxRatio = 1;
         double maxWRatio = 1;
         double minWRatio = 100;
         double meanWRatio = 0;
         json.WriteStartArray ("files");
         foreach (var doc in docs) {
            int w = doc.ReadInt ("width");
            int h = doc.ReadInt ("height");
            str = doc.ReadStr ("orientation", null);
            if (str != null) {
               var orientation = str.AsOrientation (); 
               if ((orientation & (_Orientation.Rotate90 | _Orientation.Rotate270)) != 0) {
                  int tmp = w;
                  w = h;
                  h = tmp;
               }
            }

            //Try to correct non-html5 mimetypes
            string mime = doc.ReadStr ("mime", null);
            if (mime != null && mime.StartsWith("video")) {
               if (!isSupportedHtmlVideo(mime)) {
                  string mime2 = MimeType.GetMimeTypeFromFileName (doc.Id);
                  if (isSupportedHtmlVideo (mime2)) mime = mime2;
               }
            }

            json.WriteStartObject ();
            json.WriteProperty ("f", doc.Id);
            json.WriteProperty ("a", doc.ReadStr ("album", null));
            json.WriteProperty ("y", doc.ReadStr ("year", null));
            json.WriteProperty ("w", w);
            json.WriteProperty ("h", h);
            json.WriteProperty ("mime", mime);
            json.WriteProperty ("t_nl", doc.ReadStr ("text_nl", null));
            json.WriteProperty ("t_ocr", doc.ReadStr ("ocr", null));
            writeDuration (json, doc);
            int faces = doc.ReadInt ("face_count", 0);
            if (faces>0) json.WriteProperty ("fcnt", faces);

            if (dbg) {
               json.WriteProperty ("t_en", doc.ReadStr ("text_en", null));
               json.WriteProperty ("t_txt", doc.ReadStr ("text", null));
               json.WriteProperty ("c_id", doc.ReadStr ("c_id", null));
               json.WriteProperty ("c_name", doc.ReadStr ("c_name", null));
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
            str = doc.ReadStr ("camera", null);
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

      private static void writeDuration (JsonWriter wtr, GenericDocument doc) {
         int dur = doc.ReadInt ("duration", -1);
         if (dur > 0) {
            string v = null;
            if (dur < 60) v = Invariant.Format ("00:{0:D2}", dur);
            else if (dur < 3600) v = Invariant.Format ("{0:D2}:{0:D2}", dur / 60, dur % 60);
            else {
               var m = dur % 3600;
               v = Invariant.Format ("{0:D2}:{0:D2}:{0:D2}", dur / 3600, m / 60, m % 60);
            }
            wtr.WriteProperty ("t_dur", v);
         }
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
      private static string createLargeCacheName (string id, int fp) {
         return new StringBuilder ()
            .Append (id)
            .Append ("&f=").Append (fp)
            .ToString ();
      }


      private Image<Rgb24> loadBitmap (string id, string fn) {
         string mime = MimeType.GetMimeTypeFromFileName (fn);
         if (mime != null && mime.StartsWith ("video")) {
            if (settings.VideoFrames == null) return null;
            byte[] bytes = settings.VideoFrames.GetFrame (id);
            if (bytes == null) return null;
            return Image.Load<Rgb24> (new Span<byte> (bytes));
         }
         return Image.Load<Rgb24> (fn);
      }

      static IActionResult createJpegActionResult (Stream strm, string orgFn) {
         string fn = Path.GetFileNameWithoutExtension (orgFn) + ".jpg";
         var ret = new StreamActionResult (strm,
                                           MimeType.Jpeg,
                                           fn,
                                           System.IO.File.GetLastWriteTimeUtc(orgFn),
                                           FileDisposition.Attachment);
         return ret.SetCompress (false).SetCache (CacheOptions.Private, TimeSpan.FromDays (7));
      }

      private IActionResult getSmallImage (string id, int h, string orgFn) {
         var strm = cache.FetchSmallImage (id, orgFn, h, loadBitmap);
         return createJpegActionResult (strm, orgFn);
      }

      private bool getDimensions (string id, out int w, out int h) {
         var c = settings.ESClient;
         var cmd = new StringBuilder ();
         cmd.Append (settings.MainIndex)
            .Append("/_doc/")
            .Append(Encoders.UrlDataEncode (id))
            .Append("?_source=width,height,orientation");
         var resp = c.Send (HttpMethod.Get, cmd.ToString ()); 
         if (!resp.IsOK()) {
            w = 0;
            h = 0;
            return false;
         }
         var json = resp.Json.ReadObj("_source");
         w = json.ReadInt ("width");
         h = json.ReadInt ("height");
         return true;
      }


      /// <summary>
      /// Negative values for w or h indicate a log 1.5 factor
      /// </summary>
      private IActionResult getLargeImage (string id, int w, int h, string orgFn) {
         //dumpHeaders ();
         if (w <= 0 || h <= 0) throw new BMException ("Unexpected target-size {0}x{1}", w, h);

         int srcW, srcH;
         if (!getDimensions(id, out srcW, out srcH)) goto NOT_FOUND;

         var strm = cache.FetchLargeImage (id, orgFn, srcW, srcH, w, h, loadBitmap);
         if (strm != null) return createJpegActionResult (strm, orgFn);

         //Apparently we did not shrink the result, so return the original image
         string mime = MimeType.GetMimeTypeFromFileName (orgFn);
         if (mime != null && mime.StartsWith ("video/")) {
            if (settings.VideoFrames == null) goto NOT_FOUND;
            byte[] bytes = settings.VideoFrames.GetFrame (id);
            if (bytes == null) goto NOT_FOUND;
            return createJpegActionResult(new MemoryStream (bytes), orgFn);
         }
         return PhysicalFile (orgFn, WebGlobals._GetMimeTypeForFile (orgFn), true);

      NOT_FOUND:
         return new ActionResult404 ();
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
         if (!System.IO.File.Exists (orgFn)) {
            Logs.ErrorLog.Log ("File not exists: [{0}].", orgFn);
            return new ActionResult404 ();
         }

         //Logs.DebugLog.Log ("Requesting img {0}", orgFn);
         if (w <= 0 && h <= 0 && mindim <=0) {
            return PhysicalFile (orgFn, WebGlobals._GetMimeTypeForFile (orgFn), true);
         }

         return w < 0 ? getSmallImage (id, h, orgFn) : getLargeImage (id, w, h, orgFn);
      }
      private Stream saveInCacheAndGet (Image<Rgb24> bm, string name, Shrinker shrinker , CacheType cacheType) {
         var mem = new MemoryStream ();
         bm.SaveAsJpeg (mem, shrinker.JpgEncoder);
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
         string orgFn = settings.Roots.GetRealFileName (id);
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


      public IActionResult Filename () {
         if (!RequestCtx.IsInternalIp) return new ActionResult404 ();
         string id = Request.ReadStr ("id", null);
         string fn = null;
         if (id != null) fn = ((Settings)Settings).Roots.GetRealFileName (id);
         return new JsonActionResult (new JsonObjectValue ("fn", fn));
      }



      public IActionResult Rotate () {
         string reason = "Not internal";
         if (!RequestCtx.IsInternalIp) goto RET_404;
         string id = Request.ReadStr ("id", null);
         reason = "ID is null";
         if (id==null) goto RET_404;
         string fn = ((Settings)Settings).Roots.GetRealFileName (id);
         reason = "Not found: " + fn;
         if (!System.IO.File.Exists (fn)) goto RET_404;

         int angle = Request.ReadInt ("rot", 0);
         reason = "Invalid angle: " + angle;
         switch (angle) {
            default: goto RET_404;
            case 90:
            case 180:
            case 270: break;
         }

         string result = "ok";
         try {
            var psi = new ProcessStartInfo ("exiftool", Invariant.Format ("-P -rotation={0} \"{1}\"", angle, fn));
            psi.RedirectStandardError = true;
            psi.RedirectStandardOutput = true;
            using (var p = new WrappedProcess (psi, ExifTool.Logger, WrappedProcessFlags.LogAlways)) {
               p.Start ();
               p.WaitForExit (1000);
            }
         } catch (Exception e) {
            result = e.Message;
            SiteLog.Log (e, "Rotate failed: {0}", e.Message);
         }
         return new JsonActionResult (new JsonObjectValue ("result", result));

      RET_404:
         return new ActionResult404 ();
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
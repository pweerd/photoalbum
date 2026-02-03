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
using System.Data.Common;
using System.Net;
using System.Text.RegularExpressions;

namespace AlbumImporter {
   /// <summary>
   /// Contains information about an Elasticsearch index.
   /// Used to check if indexes are the same or to get the timestamp
   /// </summary>
   public class IndexInfo {
      private static readonly Regex tsExpr = new Regex(@"(_\d{8}_\d{6})$", RegexOptions.CultureInvariant);
      public readonly string Url;
      public readonly string Name;
      public readonly ESConnection Connection;
      public readonly string Fingerprint;
      public readonly string Timestamp;

      public static IndexInfo Create(string url, bool mustExcept) {
         if (string.IsNullOrEmpty(url)) return null;
         var ret = new IndexInfo(url);
         if (ret.Fingerprint == null) {
            if (mustExcept) throw new BMException("Index for url [{0}] does not exist.", url);
            ret = null;
         }
         return ret;
      }
      public static IndexInfo Create(ESDataEndpoint ep, char oldOrNew) {
         if (ep == null) return null;
         var ret = new IndexInfo(ep, oldOrNew);
         return ret.Fingerprint == null ? null : ret;
      }

      public bool IsSameIndex(IndexInfo other) {
         return other != null && Fingerprint == other.Fingerprint;
      }

      private IndexInfo(string url) {
         Url = url;
         int len = url.Length;
         if (url[len - 1] == '/') len--;
         int ix = url.LastIndexOf ('/', len-1);
         Name = url.Substring(ix + 1, len - ix - 1);
         Connection = new ESConnection(url.Substring(0, ix));
         Fingerprint = getFingerprint(Connection, Name, out Timestamp);
      }
      private IndexInfo(ESDataEndpoint ep, char oldNew) {
         Name = oldNew=='N' ? ep.DocType.Index.IndexName : ep.DocType.Index.AliasName;
         Connection = ep.Connection;
         Url = Connection.CreateUrlPart(Name, null);
         Fingerprint = getFingerprint(Connection, Name, out Timestamp);
      }

      public override string ToString() {
         return Url;
      }

      public ESSearchRequest CreateESRequest () {
         if (Fingerprint == null) throw new BMException ("Cannot create request for [{0}]: does not exist.", Name);
         return Connection.CreateSearchRequest (Name);
      }
      public void Refresh() {
         Connection.CreateIndexRequest().Refresh (Name);
      }


      private static string getFingerprint(ESConnection c, string index, out string timestamp) {
         timestamp = null;
         var resp = c.Send (HttpMethod.Get, index + "/_settings", null);
         if (!resp.ThrowIfError(HttpStatusCode.NotFound)) return null;
         var realName = resp.Json.Keys.First();
         var match = tsExpr.Match(realName);
         if (match.Success) {
            timestamp = match.Groups[1].Value;
         }
         var realIndex = resp.Json.ReadObj(realName);
         var settingsObj = realIndex.ReadObj ("settings.index");
         var uuidIndex = settingsObj.ReadStr ("uuid");
         var created = settingsObj.ReadStr ("creation_date");

         resp = c.Send(HttpMethod.Get, "/", null);
         resp.ThrowIfError();
         var uuidCluster = resp.Json.ReadStr ("cluster_uuid");

         return uuidCluster + ":" + uuidIndex + ":" + created;
      }
   } 
}

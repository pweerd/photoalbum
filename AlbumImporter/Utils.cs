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
using Bitmanager.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace AlbumImporter {

   public class Utils {
      public static ESSearchRequest CreateESRequest (string url) {
         if (url.EndsWith ("/")) url = url.Substring (0, url.Length - 1);
         int ix = url.LastIndexOf ('/');
         string index = url.Substring (ix + 1);

         var c = new ESConnection (url.Substring (0, ix));
         return c.CreateSearchRequest (index);
      }

      public static string GetIndexName (IDataEndpoint _ep) {
         var ep = _ep as ESDataEndpoint;
         if (ep == null) return null;
         return ep.DocType.Index.IndexName;
      }

      public static string GetIndexFingerPrint (IDataEndpoint _ep) {
         var ep = _ep as ESDataEndpoint;
         if (ep == null) return null;
         string index = ep.DocType.Index.IndexName;
         return index == null ? null : Utils.GetIndexFingerPrint (ep.Connection, index);
      }

      public static string GetIndexFingerPrint(ESConnection c, string index) {
         var resp = c.Send (HttpMethod.Get, index + "/_settings", null);
         if (!resp.ThrowIfError (HttpStatusCode.NotFound)) return null;

         var realIndex = (JsonObjectValue)resp.Json.Values.First();
         var settingsObj = realIndex.ReadObj ("settings.index");
         var uuidIndex = settingsObj.ReadStr ("uuid");
         var created = settingsObj.ReadStr ("creation_date");

         resp = c.Send (HttpMethod.Get, "/", null);
         resp.ThrowIfError ();
         var uuidCluster = resp.Json.ReadStr ("cluster_uuid");

         return uuidCluster + ":" + uuidIndex + ":" + created;
      }

      public static string GetRealIndexName (ESDataEndpoint ep, string index, out string timestamp) {
         if (index == null) index = ep.DocType.Index.IndexName;
         if (index == null) throw new Exception ("Index name could not be extracted from endpoint.");

         var resp = ep.Connection.Send (HttpMethod.Get, index + "/_settings", null);
         if (resp.StatusCode== HttpStatusCode.NotFound) {
            timestamp = null;
            return null;
         }

         resp.ThrowIfError ();
         string realName = resp.Json.Keys.First ();
         string ts = realName.Substring (realName.Length - 16);
         if (ts[0] != '_') throw new BMException ("Incorrect formatted indexname: [{0}]: timestamp=[{1}].",
            realName, ts);
         timestamp = ts;
         return realName;
      }


      public static RootReplacer CreateRootReplacer (XmlNode node) {
         var rootsNode = node.SelectSingleNode ("roots");
         if (rootsNode == null) {
            rootsNode = node.OwnerDocument.DocumentElement.SelectSingleNode ("roots");
            if (rootsNode == null)
               throw new BMNodeException (node, "Cannot find roots-node (neither on a global level).");
         }
         return new RootReplacer (rootsNode);
      }

      public static string GetTimestamp (string name) {
         return name.Substring (name.Length - 16, 16);
      }
      public static string GetIndexWithoutTimeStamp (string name) {
         return name.Substring (0, name.Length - 16);
      }

   }
}

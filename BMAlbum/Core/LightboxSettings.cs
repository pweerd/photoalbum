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

using Bitmanager.Core;
using Bitmanager.Json;
using Bitmanager.Xml;
using System.Xml;

namespace BMAlbum.Core {

   //NB these constants should match the constants in site.js
   public enum BrowserType {
      None= 0,
      Desktop=1,
      Phone=2,
      Tablet=4,
      Mobile=6,
      All = 7
   }
   public class LightboxSettings {
      public const int DEF_MINCOUNT_ALBUM = 4;
      public readonly int PageSize;
      public readonly int MinCountForAlbum;
      public readonly int PreloadBackward, PreloadForward;

      public readonly string CacheVersion;
      public readonly bool Paginate;
      public readonly JsonObjectValue SettingsForClient;


      public LightboxSettings (XmlNode node) {
         PreloadBackward = node.ReadInt ("preload/@backward", 1);
         PreloadForward = node.ReadInt ("preload/@forward", 1);
         XmlNodeList sizesNodes = null;
         BrowserType squareOn = BrowserType.None;
         if (node==null) {
            PageSize = 100;
            MinCountForAlbum = DEF_MINCOUNT_ALBUM;
            Paginate = false;
         } else {
            PageSize = node.ReadInt("@pagesize", 100);
            MinCountForAlbum = node.ReadInt ("@album_mincount", DEF_MINCOUNT_ALBUM);
            if (MinCountForAlbum < 0) MinCountForAlbum = DEF_MINCOUNT_ALBUM;
            Paginate = node.ReadBool ("@paginate", false);
            CacheVersion = node.ReadStr ("@cache_version", null);
            if (CacheVersion != null) {
               if (!CacheVersion.StartsWith ("&v=")) CacheVersion = "&v=" + CacheVersion;
            }

            var tmp = node.SelectSingleNode ("sizes");
            squareOn = tmp.ReadEnum ("@square_on", BrowserType.None);
            sizesNodes = node.SelectNodes ("sizes");
         }

         SettingsForClient = createClientSettings (sizesNodes);
      }

      private JsonObjectValue createClientSettings (XmlNodeList deviceNodes) {
         var ret = new JsonObjectValue ("backward", PreloadBackward, "forward", PreloadForward);
         ret = new JsonObjectValue ("preload", ret);

         JsonArrayValue sizeSettings;
         if (deviceNodes == null || deviceNodes.Count == 0) 
            sizeSettings = createDefaultSizeSettings ();
         else {
            sizeSettings = new JsonArrayValue ();
            int N = deviceNodes.Count - 1;
            for (int i = 0; i <= N; i++) {
               XmlNode devNode = deviceNodes[i];
               var obj = new JsonObjectValue ();
               var devType = devNode.ReadEnum<BrowserType> ("@device");
               if (i == N && devType != BrowserType.All)
                  throw new BMNodeException (devNode, "Last node needs to have device='all'.");
               var sizeNodes = devNode.SelectMandatoryNodes ("size");
               var sizes = new JsonArrayValue ();
               for (int j = 0; j < sizeNodes.Count; j++) {
                  XmlNode sub = sizeNodes[j];
                  if (j == 0 && 0 != sub.ReadInt ("@width"))
                     throw new BMNodeException (sub, "First node needs to have width='0'.");
                  sizes.Add (createSizeSettings (sub));
               }
               sizeSettings.Add (new JsonObjectValue ("device", (int)devType, "sizes", sizes));
            }
         }
         ret["devices"] = sizeSettings;
         return ret;
      }

      private static JsonArrayValue createDefaultSizeSettings () {
         var ret = new JsonArrayValue ();

         var arr = new JsonArrayValue ();
         arr.Add (createSizeSettings (0, 3, "1", 0, ""));
         arr.Add (createSizeSettings (400, 4, "1", 0, ""));
         ret.Add (new JsonObjectValue ("device", (int)BrowserType.Phone, "sizes", arr));

         arr = new JsonArrayValue ();
         arr.Add (createSizeSettings (0, 2, "3:4", 0, "space-between"));
         arr.Add (createSizeSettings (512, 3, "3:4", 0, "space-between"));
         arr.Add (createSizeSettings (1024, 4, "3:4", 0, "space-between"));
         ret.Add (new JsonObjectValue ("device", (int)BrowserType.All, "sizes", arr));

         return ret;
      }

      private static JsonObjectValue createSizeSettings (XmlNode node) {
         var obj = new JsonObjectValue ();
         obj["width"] = node.ReadInt ("@width");
         obj["target_count"] = node.ReadInt ("@target_count", 0);
         var ratio = toRatio (node.ReadStr ("@max_ratio", ""));
         obj["ratio_lo"] = ratio;
         obj["ratio_hi"] = 1 / ratio;
         obj["fixed"] = node.ReadInt ("@fixed", 0);

         var attr = node.Attributes;

         JsonObjectValue jsonAttr = new JsonObjectValue ();
         obj["attr"] = jsonAttr;
         bool foundJustifyContent = false;
         foreach (XmlAttribute a in node.Attributes) {
            if (a.LocalName.StartsWith ("attr_")) {
               string name = a.LocalName.Substring (5).Replace('_', '-');
               if (name == "justify-content") foundJustifyContent = true;
               jsonAttr[name] = a.Value.Trim();
            }
         }
         if (!foundJustifyContent) jsonAttr["justify-content"] = "";
         return obj;
      }
      private static JsonObjectValue createSizeSettings (int width, int targetCount, string ratio, int fixedHeight, string justifyContent) {
         var obj = new JsonObjectValue ();
         obj["width"] = width;
         obj["target_count"] = targetCount;
         var ratioVal = toRatio (ratio);
         obj["ratio_lo"] = ratioVal;
         obj["ratio_hi"] = 1 / ratioVal;
         obj["fixed"] = fixedHeight;
         obj["square_on"] = (int)BrowserType.Phone;

         JsonObjectValue jsonAttr = new JsonObjectValue ();
         obj["attr"] = jsonAttr;
         if (!string.IsNullOrEmpty(justifyContent)) jsonAttr["justify_content"] = justifyContent;
         return obj;
      }

      private static double toRatio (string v) {
         int ix = v.IndexOf (':');
         if (ix < 0) return Invariant.ToFloat (v);
         var aspect = Invariant.ToFloat (v.Substring (0, ix)) / Invariant.ToFloat (v.Substring (ix + 1));
         return aspect <= 1 ? aspect : 1 / aspect;
      }
   }
}

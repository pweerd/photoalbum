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
using System.Data;
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
      public readonly DeviceSizeSettings[] DeviceSizes;
      public readonly string CacheVersion;
      public readonly bool Paginate;


      public LightboxSettings (XmlNode node) {
         PreloadBackward = node.ReadInt ("preload/@backward", 1);
         PreloadForward = node.ReadInt ("preload/@forward", 1);
         XmlNodeList sizesNodes = null;
         BrowserType squareOn = BrowserType.None;
         if (node==null) {
            PageSize = 100;
            MinCountForAlbum = DEF_MINCOUNT_ALBUM;
            Paginate = false;
            DeviceSizes = createDefaultDeviceSettings ();
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
            DeviceSizes = createDeviceSettings (node);
         }
      }

      public SizeSettings[] GetSizeSettingsForDevice(BrowserType type) {
         for (int i=0; i<DeviceSizes.Length; i++) {
            if ((DeviceSizes[i].Type & type) != 0) return DeviceSizes[i].SizeSettings;
         }
         throw new BMException ("Could not find SizeSettings for type [{0}]", type);
      }



      public void WriteClientConfig (JsonWriter json, BrowserType type) {
         json.WriteStartObject ();
         json.WriteStartObject ("preload");
         json.WriteProperty ("backward", PreloadBackward);
         json.WriteProperty ("forward", PreloadForward);
         json.WriteEndObject ();

         json.WriteStartArray ("sizes");
         var sizes = this.GetSizeSettingsForDevice (type);
         for(int i=0; i<sizes.Length; i++) json.WriteValue (sizes[i]);
         json.WriteEndArray ();

         json.WriteEndObject ();
      }



      private DeviceSizeSettings[] createDefaultDeviceSettings () {
         DeviceSizeSettings[] ret = new DeviceSizeSettings[2];
         var arr = new SizeSettings[2];
         arr[0] = new SizeSettings (0, 3, "1", 0, string.Empty);
         arr[1] = new SizeSettings (400, 4, "1", 0, string.Empty);
         ret[0] = new DeviceSizeSettings (BrowserType.Phone, arr);

         arr = new SizeSettings[3];
         arr[0] = new SizeSettings (0, 2, "3:4", 0, "space-between");
         arr[1] = new SizeSettings (512, 3, "3:4", 0, "space-between");
         arr[2] = new SizeSettings (1024, 4, "3:4", 0, "space-between");
         ret[1] = new DeviceSizeSettings (BrowserType.All, arr);

         return ret;
      }

      private DeviceSizeSettings[] createDeviceSettings (XmlNode mainNode) {
         if (mainNode == null) return createDefaultDeviceSettings ();
         var list = mainNode.SelectNodes ("sizes");
         if (list.Count==0) return createDefaultDeviceSettings ();

         DeviceSizeSettings[] ret = new DeviceSizeSettings [list.Count];
         for (int i = 0; i < ret.Length; i++) {
            ret[i] = new DeviceSizeSettings (list[i]);
         }
         int last = ret.Length - 1;
         if (ret[last].Type != BrowserType.All)
            throw new BMNodeException (list[last], "Last node needs to have device='all'.");

         return ret;
      }


      public class DeviceSizeSettings {
         public readonly BrowserType Type;
         public readonly SizeSettings[] SizeSettings;
         
         public DeviceSizeSettings(BrowserType type, SizeSettings[] arr) {
            Type = type;
            SizeSettings = arr;
         }
         public DeviceSizeSettings (XmlNode node) {
            Type = node.ReadEnum<BrowserType> ("@device");
            var sizeNodes = node.SelectMandatoryNodes ("size");
            SizeSettings = new SizeSettings[sizeNodes.Count];
            int i;
            for (i=0; i<sizeNodes.Count; i++) {
               SizeSettings[i] = new SizeSettings (sizeNodes[i]);
            }
            if (SizeSettings[0].Width != 0)
               throw new BMNodeException (sizeNodes[0], "First node needs to have width='0'.");
         }
      }

      public class SizeSettings: IJsonSerializable {
         public readonly int Width;
         public readonly int TargetCount;
         public readonly float Ratio;
         public readonly int Fixed;
         public readonly Dictionary<string,string> Attributes;

         //Constructor used for defaults
         public SizeSettings (int width, int targetCount, string ratio, int fixedHeight, string justifyContent) {
            Width = width;
            TargetCount = targetCount;
            Ratio = toRatio (ratio);
            Fixed = fixedHeight;

            Attributes = new Dictionary<string, string> (1);
            Attributes["justify_content"] = justifyContent;
         }

         //Constructor used for loading from Xml
         public SizeSettings (XmlNode node) {
            Width = node.ReadInt ("@width");
            TargetCount = node.ReadInt ("@target_count", 0);
            Ratio = toRatio (node.ReadStr ("@max_ratio", ""));
            Fixed = node.ReadInt ("@fixed", 0);

            Attributes = new Dictionary<string,string> ();
            bool foundJustifyContent = false;
            foreach (XmlAttribute a in node.Attributes) {
               if (a.LocalName.StartsWith ("attr_")) {
                  string name = a.LocalName.Substring (5).Replace ('_', '-');
                  if (name == "justify-content") foundJustifyContent = true;
                  Attributes[name] = a.Value.Trim ();
               }
            }
            if (!foundJustifyContent) Attributes["justify-content"] = string.Empty;
         }

         private static float toRatio (string v) {
            int ix = v.IndexOf (':');
            if (ix < 0) return Invariant.ToFloat (v);
            var aspect = Invariant.ToFloat (v.Substring (0, ix)) / Invariant.ToFloat (v.Substring (ix + 1));
            return aspect <= 1 ? aspect : 1 / aspect;
         }


         public void WriteTo (JsonWriter wtr) {
            wtr.WriteStartObject ();
            wtr.WriteProperty ("width", Width);
            wtr.WriteProperty ("target_count", TargetCount);
            wtr.WriteProperty ("ratio_lo", Ratio);
            wtr.WriteProperty ("ratio_hi", 1/Ratio);
            wtr.WriteProperty ("fixed", Fixed);
            wtr.WriteStartObject ("attr");
            foreach (var kvp in Attributes) {
               wtr.WriteProperty(kvp.Key, kvp.Value);
            }
            wtr.WriteEndObject ();
            wtr.WriteEndObject ();
         }
      }

   }
}

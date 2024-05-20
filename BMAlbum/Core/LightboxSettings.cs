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

using Bitmanager.Xml;
using System.Xml;

namespace BMAlbum.Core {
   public class LightboxSettings {
      public readonly int PageSize;
      public readonly int MinCountForAlbum;

      public readonly string CacheVersion;
      public readonly bool Paginate;


      public LightboxSettings (XmlNode node) {
         if (node==null) {
            PageSize = 100;
            MinCountForAlbum = 4;
            Paginate = false;
         } else {
            PageSize = node.ReadInt("@pagesize", 100);
            MinCountForAlbum = node.ReadInt ("@album_mincount", 4);
            Paginate = node.ReadBool ("@paginate", false);
            CacheVersion = node.ReadStr ("@cache_version", null);
            if (CacheVersion != null) {
               if (!CacheVersion.StartsWith ("&v=")) CacheVersion = "&v=" + CacheVersion;
            }
         }
      }
   }
}

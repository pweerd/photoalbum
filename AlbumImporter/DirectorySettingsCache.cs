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
using Bitmanager.Xml;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AlbumImporter {
   public class DirectorySettingsCache {
      public readonly DirectorySettings Default;
      public ICollection<string> Keys => cache.Keys;

      private Dictionary<string, DirectorySettings> cache; 

      public DirectorySettingsCache (DirectorySettings def) {
         Default = def != null ? def : new DirectorySettings ();
         cache = new Dictionary<string, DirectorySettings> ();
      }

      public DirectorySettings GetSettings (string dir, int rootLen) {
         if (string.IsNullOrEmpty(dir) || dir.Length < rootLen) return Default;

         if (cache.TryGetValue (dir, out var settings)) return settings;

         var parentSettings = GetSettings (Path.GetDirectoryName (dir), rootLen);
         string fn = Path.Combine (dir, "importsettings.xml");
         try {
            if (File.Exists (fn)) {
               var xml = new XmlHelper (fn);
               settings = new DirectorySettings (xml, parentSettings, rootLen);
            } else {
               settings = new DirectorySettings (dir, parentSettings, rootLen);
            }
         } catch (Exception e) {
            throw new BMException (e, "{0}\nFile={1}.", e.Message, fn);
         }
         if ("hidden".Equals (Path.GetFileName (dir), StringComparison.InvariantCultureIgnoreCase))
            settings = new DirectorySettings (settings, _HideStatus.Always);
         cache.Add (dir, settings);
         return settings;
      }
   }
}

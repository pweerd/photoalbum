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
using System.Xml;

namespace AlbumImporter {

   public enum _HideStatus { None, External, Always }
   [Flags]
   public enum _Inherit { FromDefault = 0, FromParent=1 }
   public enum _Propagate { This=0, Default=1, Parent= 2 }


   //   <root inherit = "true" >
   //     <album minlen="2" force="" take_from_dir="true"/>
   //     <date force = "" type="utc|local" />
   //     <camera force = "scanner" />
   //   </ root >
   public class DirectorySettings {
      public static readonly DirectorySettings Default = new DirectorySettings();
      public readonly DirectorySettings Parent;
      public readonly _Inherit Inherit;
      public readonly _Propagate Propagate;
      public readonly _HideStatus HideStatus;
      public readonly string ForcedUser;

      public readonly DateFetcher DateFetcher;
      public readonly AlbumFetcher AlbumFetcher;
      public readonly CameraFetcher CameraFetcher;
      public readonly WhatsappDetector WhatsappDetector;

      internal string RelName;
      internal Dictionary<string, AlbumCache> AlbumCache;
      internal string CachedAlbum; //PW nakijken
      internal DateTime CachedDate;
      internal int CachedFileOrder;


      public DirectorySettings () {
         Inherit = _Inherit.FromDefault;
         Propagate = _Propagate.This;

         DateFetcher = DateFetcher.DefaultAssumeLocal;
         AlbumFetcher = AlbumFetcher.Default;
         CameraFetcher = CameraFetcher.Default;
         WhatsappDetector = WhatsappDetector.Default;
      }


      private DirectorySettings getPropagationParent(_Inherit inherit) {
         if (inherit == _Inherit.FromDefault) return Default;
         var p = this;
         for (; p != null; p = p.Parent) {
            switch (p.Propagate) {
               case _Propagate.This: return p;
               case _Propagate.Default: return Default;
               case _Propagate.Parent: continue;
               default:
                  p.Propagate.ThrowUnexpected ();
                  break;
            }
         }
         return Default;
      }
      public DirectorySettings (DirectorySettings other, _HideStatus hideStatus) {
         AlbumCache = new Dictionary<string, AlbumCache> ();

         HideStatus = hideStatus;
         Parent = other.Parent;
         Inherit = other.Inherit;
         Propagate = other.Propagate;
         ForcedUser = other.ForcedUser;

         DateFetcher = other.DateFetcher;
         AlbumFetcher = other.AlbumFetcher;
         CameraFetcher = other.CameraFetcher;
         WhatsappDetector = other.WhatsappDetector;

         RelName = other.RelName;
      }

      public DirectorySettings (string name, DirectorySettings def, int rootLen) : this (def, def.HideStatus) {
         Parent = def;
         RelName = name.Substring (rootLen);
      }

      public DirectorySettings (XmlHelper xml, DirectorySettings def, int rootLen) : this (xml.DocumentElement, def) {
         string name = Path.GetDirectoryName (xml.FileName);
         RelName = name.Substring (rootLen);
      }

      public DirectorySettings (XmlNode node, DirectorySettings def) {
         AlbumCache = new Dictionary<string, AlbumCache> ();

         Propagate = node.ReadEnum ("@propagate", _Propagate.This);
         Inherit = node.ReadEnum ("@inherit", _Inherit.FromParent);
         def = def.getPropagationParent(Inherit);
         Parent = def;
         HideStatus = node.ReadEnum ("@hide", def.HideStatus);
         ForcedUser = node.ReadStr ("@user", def.ForcedUser);

         DateFetcher = DateFetcher.Create (node.SelectSingleNode ("date"), def.DateFetcher);
         AlbumFetcher = AlbumFetcher.Create (node.SelectSingleNode ("album"), def.AlbumFetcher);
         CameraFetcher = CameraFetcher.Create (node.SelectSingleNode ("camera"), def.CameraFetcher);
         WhatsappDetector = WhatsappDetector.Create (node.SelectSingleNode ("whatsapp"), def.WhatsappDetector);
      }


   }

   public class AlbumCache {
      public readonly string Album;
      public readonly string YearPlusAlbum;
      public readonly DateTime AlbumTime;
      public AlbumCache (string album, string yearPlusAlbum, DateTime date) {
         Album = album;
         YearPlusAlbum = yearPlusAlbum;
         AlbumTime = date;
      }
   }
}

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
using Bitmanager.ImageTools;
using Bitmanager.Xml;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace AlbumImporter.Fetchers {
   public enum _WhatsappSource { None, Value, FromNoThumbnail};
   public class WhatsappDetector {
      protected readonly string OverrideAlbum;
      protected readonly string Source;
      protected readonly bool Value;

      public static readonly WhatsappDetector Default = new FromNoThumbnailWhatsappDetector ("FromNoThumbnail", "Whatsapp", false);

      protected WhatsappDetector (string strSource, string overrideAlbum, bool value) {
         OverrideAlbum = overrideAlbum;
         Source = strSource;
         Value = value;
      }

      public virtual bool DetectWhatsapp (IdInfo info, Metadata md, ref string album) {
         return false;
      }

      public override string ToString () {
         return Invariant.Format ("{0} [src={1}, value={2}, override_album={3}]",
            GetType ().Name, Source, Value, OverrideAlbum);
      }

      public static WhatsappDetector Create (XmlNode node, WhatsappDetector def) {
         if (def == null) def = Default;
         if (node == null) return def;
         var val = node.ReadBool ("@value", def.Value);
         var src = node.ReadStr ("@src", def.Source);
         var album = node.ReadStrRaw ("@override_album", 
                                      _XmlRawMode.EmptyToNull | _XmlRawMode.DefaultOnNull | _XmlRawMode.Trim, 
                                      def.OverrideAlbum);
         node.CheckInvalidAttributes ("src", "value", "override_album");
         return WhatsappDetector.Create (src, album, val);
      }


      public static WhatsappDetector Create (string strSource, string overrideAlbum, bool value) {
         var list = new List<WhatsappDetector> ();
         string[] flags = strSource.SplitStandard ();
         if (flags.Length == 0) return createOne (_WhatsappSource.None, strSource, overrideAlbum, value);

         WhatsappDetector[] fetchers = new WhatsappDetector[flags.Length];
         for (int i = 0; i < flags.Length; i++) {
            var src = Invariant.ToEnum<_WhatsappSource> (flags[i]);
            fetchers[i] = createOne (src, strSource, overrideAlbum, value);
         }

         return fetchers.Length == 1 ? fetchers[0] : new MultiWhatsappDetector (fetchers, strSource, overrideAlbum, value);
      }

      public static WhatsappDetector createOne (_WhatsappSource src, string strSource, string overrideAlbum, bool value) {

         switch (src) {
            case _WhatsappSource.None: return new WhatsappDetector (strSource, overrideAlbum, value);
            case _WhatsappSource.FromNoThumbnail: return new FromNoThumbnailWhatsappDetector (strSource, overrideAlbum, value);
            case _WhatsappSource.Value: return new FromValueWhatsappDetector (strSource, overrideAlbum, value);
         }
         src.ThrowUnexpected ();
         return null;
      }

   }

   public class MultiWhatsappDetector : WhatsappDetector {
      private readonly WhatsappDetector[] fetchers;
      public MultiWhatsappDetector (WhatsappDetector[] list, string strSource, string overrideAlbum, bool value) 
         : base (strSource, overrideAlbum, value) {
         fetchers = list;
      }

      public override bool DetectWhatsapp (IdInfo info, Metadata md, ref string album) {
         bool ret = false;

         for (int i = 0; i < fetchers.Length; i++) {
            ret |= fetchers[i].DetectWhatsapp (info, md, ref album);
         }
         return ret;
      }
   }



   public class FromValueWhatsappDetector : WhatsappDetector {
      public FromValueWhatsappDetector (string strSource, string overrideAlbum, bool value) 
         : base (strSource, overrideAlbum, value) {
      }

      public override bool DetectWhatsapp (IdInfo info, Metadata md, ref string album) {
         if (Value) {
            if (OverrideAlbum != null) album = OverrideAlbum;
            return true;
         }
         return false;
      }
   }

   public class FromNoThumbnailWhatsappDetector : WhatsappDetector {
      public FromNoThumbnailWhatsappDetector (string strSource, string overrideAlbum, bool value)
         : base (strSource, overrideAlbum, value) { }

      public override bool DetectWhatsapp (IdInfo info, Metadata md, ref string album) {
         if (md.ThumbnailLength > 0) return false;
         if (OverrideAlbum != null) album = OverrideAlbum;
         return true;
      }
   }
}

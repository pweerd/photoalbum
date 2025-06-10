using Bitmanager.Core;
using Bitmanager.ImageTools;
using Bitmanager.Xml;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace AlbumImporter {
   public enum _CameraSource { None, FromMetadata, FromMeta=FromMetadata, FromValue}
   public class CameraFetcher {
      public static readonly List<string> EMPTY = new List<string> (0);
      public static readonly CameraFetcher Default = new FromMetadataCameraFetcher ("FromMeta", null);

      protected readonly string Source, Value;

      protected CameraFetcher (string strSource, string value) {
         Source = strSource; 
         Value = value.TrimToNull();
      }

      public virtual void GetCamera (List<string> dst, IdInfo info, Metadata md) {
      }


      public override string ToString () {
         return Invariant.Format ("{0} [src={1}, value={2}]",
            GetType ().Name, Source, Value);
      }

      public static CameraFetcher Create (XmlNode node, CameraFetcher def) {
         if (def == null) def = Default;
         if (node == null) return def;
         var val = node.ReadStr ("@value", def.Value);
         var src = node.ReadStr ("@src", def.Source);
         node.CheckInvalidAttributes ("src", "value");
         return CameraFetcher.Create (src, val);
      }

      public static CameraFetcher Create (string strSource, string value) {
         string[] flags = strSource.SplitStandard ();
         if (flags.Length == 0) return createOne (_CameraSource.FromMetadata, strSource, null);
         CameraFetcher[] fetchers = new CameraFetcher[flags.Length];
         for (int i = 0; i < flags.Length; i++) {
            var src = Invariant.ToEnum<_CameraSource> (flags[i]);
            fetchers[i] = createOne (src, strSource, value);
         }

         return fetchers.Length == 1 ? fetchers[0] : new MultiCameraFetcher (fetchers, strSource, value);

      }

      public static CameraFetcher createOne (_CameraSource src, string strSource, string value) {

         switch (src) {
            case _CameraSource.None: return new CameraFetcher (strSource, value);
            case _CameraSource.FromMetadata: return new FromMetadataCameraFetcher (strSource, value);
            case _CameraSource.FromValue: return new FromValueCameraFetcher (strSource, value);
         }
         src.ThrowUnexpected ();
         return null;
      }

   }

   public class MultiCameraFetcher : CameraFetcher {
      private readonly CameraFetcher[] fetchers;

      public MultiCameraFetcher (CameraFetcher[] fetchers, string strSource, string value): base (strSource, value) {
         this.fetchers = fetchers;
      }
      public override void GetCamera (List<string> dst, IdInfo info, Metadata md) {
         for (int i = 0; i < fetchers.Length; i++)
            fetchers[i].GetCamera (dst, info, md);
      }
   }

   public class FromMetadataCameraFetcher: CameraFetcher {
      public FromMetadataCameraFetcher (string strSource, string value) : base (strSource, value) {
      }

      public override void GetCamera (List<string> dst, IdInfo info, Metadata md) {
         if (md.Model != null) {
            if (md.Make != null && !md.Model.Contains (md.Make)) {
               dst.Add (md.Make + " " + md.Model);
               return;
            }
            dst.Add (md.Model);
            return;
         }
         if (md.Make != null)
            dst.Add (md.Make);
      }
   }

   public class FromValueCameraFetcher : CameraFetcher {

      public FromValueCameraFetcher (string strSource, string value) : base(strSource, value) {
         if (value == null) throw new BMException ("Camera value cannot be empty if src=FromValue!");
      }
      public override void GetCamera (List<string> dst, IdInfo info, Metadata md) {
         dst.Add (Value);
      }
   }

}

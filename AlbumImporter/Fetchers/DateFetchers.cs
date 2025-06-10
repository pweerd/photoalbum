using Bitmanager.Core;
using Bitmanager.ImageTools;
using Bitmanager.Xml;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace AlbumImporter {

   public enum _DateSource { None, FromMetaCreate, FromMetaOriginal, FromMetaLowest, FromMetadata=FromMetaLowest, FromFileName, FromFileDate, FromDirectoryName, FromValue, Value=FromValue, AssumeUtc, AssumeLocal, Default };
   //public enum _AlbumSource { None, FromFileName, FromDirectory, Whatapp, Value };
   //public enum _CameraSource { None, FromImage, Value };

   public abstract class DateFetcher {
      public static readonly string DefaultFlags = "FromMetaLowest,FromFileName,FromDirectoryName";
      public static readonly DateFetcher DefaultAssumeLocal = DateFetcher.Create (DefaultFlags);
      protected readonly string Source;
      protected readonly DateTime Value;
      protected readonly bool AssumeLocalInUnspecified;
      protected DateFetcher (string strSource, DateTime dt, bool assumeLocalInUnspecified) {
         Source = strSource;
         AssumeLocalInUnspecified = assumeLocalInUnspecified;
         Value = ToUtc (dt);
      }

      protected DateTime ToUtc (DateTime dt) {
         if (dt != DateTime.MinValue) {
            switch (dt.Kind) {
               case DateTimeKind.Utc: break;
               case DateTimeKind.Local: return dt.ToUniversalTime ();
               case DateTimeKind.Unspecified:
                  if (AssumeLocalInUnspecified) return dt.ToUniversalTime ();
                  return dt.SetKind (DateTimeKind.Utc);
            }
         }
         return dt;
      }

      public abstract DateTime GetDate (IdInfo id, DirectorySettings dirSettings, Metadata md);

      public override string ToString () {
         return Invariant.Format ("{0} [src={1}, value={2}]",
            GetType ().Name, Source, Value==DateTime.MinValue ? "" : Invariant.ToString(Value));
      }

      public static DateFetcher Create (XmlNode node, DateFetcher def) {
         if (def == null) def = DefaultAssumeLocal;
         if (node == null) return def;
         var val = node.ReadDate ("@value", def.Value);
         var src = node.ReadStr ("@src", def.Source);
         node.CheckInvalidAttributes ("src", "value");
         return DateFetcher.Create (src, val);
      }

      public static DateFetcher Create (string strFlags) {
         return Create (strFlags, DateTime.MinValue);
      }
      public static DateFetcher Create (string strSource, DateTime dt) {
         var list = new List<DateFetcher> ();
         bool assumeLocal = true;
         create (list, strSource, dt, ref assumeLocal);

         switch (list.Count) {
            case 0: return createOne (dt == DateTime.MinValue ? _DateSource.None : _DateSource.Value, strSource, dt, true);
            case 1: return list[0];
            default: return new MultiDateFetcher (list, strSource, dt, list[0].AssumeLocalInUnspecified);
         }
      }
      private static void create (List<DateFetcher> list, string strSource, DateTime dt, ref bool assumeLocal) {
         string[] flags = strSource.SplitStandard ();
         if (flags != null) {
            for (int i = 0; i < flags.Length; i++) {
               _DateSource src = Invariant.ToEnum<_DateSource> (flags[i]);
               switch (src) {
                  case _DateSource.AssumeLocal: assumeLocal = true; continue;
                  case _DateSource.AssumeUtc: assumeLocal = false; continue;
                  case _DateSource.Default: create (list, DefaultFlags, dt, ref assumeLocal); continue;
               }
               list.Add (createOne (src, strSource, dt, assumeLocal));
            }
         }
      }
      private static DateFetcher createOne (_DateSource src, string strSource, DateTime dt, bool assumeLocal) {
         DateFetcher ret = null;
         switch (src) {
            case _DateSource.None: ret = new DateNoneFetcher (strSource, dt, assumeLocal); break;
            case _DateSource.Value: ret = new DateValueFetcher (strSource, dt, assumeLocal); break;
            case _DateSource.FromFileName: ret = new FileNameDateFetcher (strSource, dt, assumeLocal); break;
            case _DateSource.FromDirectoryName: ret = new DirectoryNameDateFetcher (strSource, dt, assumeLocal); break;
            case _DateSource.FromFileDate: ret = new FileDateFetcher (strSource, dt, assumeLocal); break;
            case _DateSource.FromMetaCreate: ret = new MetaCreateDateFetcher (strSource, dt, assumeLocal); break;
            case _DateSource.FromMetaOriginal: ret = new MetaOriginalDateFetcher (strSource, dt, assumeLocal); break;
            case _DateSource.FromMetaLowest: ret = new MetaLowestDateFetcher (strSource, dt, assumeLocal); break;
               
            default: src.ThrowUnexpected (); break;
         }
         return ret;
      }

      protected static bool tryParseDate (string date, ref int yy, ref int mm, ref int dd) {
         //var sb 
         int dt;
         switch (date.Length) {
            default: goto NOT_VALID;
            case 8:
               if (Invariant.TryParse2 (date, out dt) != TryParseResult.Ok) goto NOT_VALID;
               return tryParseDate (dt, ref yy, ref mm, ref dd);
            case 10:
               if (Invariant.TryParse2 (date, out dt) != TryParseResult.Ok) goto NOT_VALID;
               return tryParseDate (dt, ref yy, ref mm, ref dd);
         }

      NOT_VALID:
         return false;
      }
      protected static bool tryParseDate (int date, ref int yy, ref int mm, ref int dd) {
         int _y = date / 10000;
         if (_y < 1900 || _y > 2100) return false;
         int _m = (date / 100) % 100;
         if (_m < 1 || _m > 12) return false;
         int _d = date % 100;
         if (_d < 1 || _d > 31) return false;
         yy = _y;
         mm = _m;
         dd = _d;
         return true;
      }



      public class MultiDateFetcher : DateFetcher {
         private readonly DateFetcher[] fetchers;
         public MultiDateFetcher (List<DateFetcher> list, string strSource, DateTime dt, bool assumeLocalInUnspecified) 
            : base (strSource, dt, assumeLocalInUnspecified) {
            fetchers = list.ToArray ();
         }

         public override DateTime GetDate (IdInfo id, DirectorySettings dirSettings, Metadata md) {
            for (int i = 0; i < fetchers.Length; i++) {
               var dt = fetchers[i].GetDate (id, dirSettings, md);
               if (dt != DateTime.MinValue) return dt;
            }
            return DateTime.MinValue;
         }
      }


      public class DateNoneFetcher : DateFetcher {
         public DateNoneFetcher (string strSource, DateTime dt, bool assumeLocalInUnspecified)
            : base (strSource, dt, assumeLocalInUnspecified) {
         }

         public override DateTime GetDate (IdInfo id, DirectorySettings dirSettings, Metadata md) {
            return DateTime.MinValue;
         }
      }

      public class DateValueFetcher : DateFetcher {
         public DateValueFetcher (string strSource, DateTime dt, bool assumeLocalInUnspecified)
            : base (strSource, dt, assumeLocalInUnspecified) {
         }

         public override DateTime GetDate (IdInfo id, DirectorySettings dirSettings, Metadata md) {
            return Value;
         }
      }

      public class FileDateFetcher : DateFetcher {
         public FileDateFetcher (string strSource, DateTime dt, bool assumeLocalInUnspecified)
            : base (strSource, dt, assumeLocalInUnspecified) {
         }

         public override DateTime GetDate (IdInfo id, DirectorySettings dirSettings, Metadata md) {
            return id.DateUtc;
         }
      }

      public class MetaCreateDateFetcher : DateFetcher {
         public MetaCreateDateFetcher (string strSource, DateTime dt, bool assumeLocalInUnspecified)
            : base (strSource, dt, assumeLocalInUnspecified) {
         }

         public override DateTime GetDate (IdInfo id, DirectorySettings dirSettings, Metadata md) {
            return ToUtc (md.DateCreated);
         }
      }

      public class MetaOriginalDateFetcher : DateFetcher {
         public MetaOriginalDateFetcher (string strSource, DateTime dt, bool assumeLocalInUnspecified)
            : base (strSource, dt, assumeLocalInUnspecified) {
         }

         public override DateTime GetDate (IdInfo id, DirectorySettings dirSettings, Metadata md) {
            return ToUtc (md.DateOriginal);
         }
      }
      public class MetaLowestDateFetcher : DateFetcher {
         public MetaLowestDateFetcher (string strSource, DateTime dt, bool assumeLocalInUnspecified)
            : base (strSource, dt, assumeLocalInUnspecified) {
         }

         public override DateTime GetDate (IdInfo id, DirectorySettings dirSettings, Metadata md) {
            var x = md.DateCreated;
            if (x == DateTime.MinValue) return ToUtc (md.DateOriginal);

            x = ToUtc (x);
            if (md.DateOriginal != DateTime.MinValue) {
               DateTime y = ToUtc (md.DateOriginal);
               if (y < x) x = y;
            }
            return x;
         }
      }

      public class FileNameDateFetcher : DateFetcher {
         private readonly DateParser2 dateParser;
         public FileNameDateFetcher (string strSource, DateTime dt, bool assumeLocalInUnspecified)
            : base (strSource, dt, assumeLocalInUnspecified) {
            dateParser = new DateParser2 (DateParserFlags.None);
         }

         public override DateTime GetDate (IdInfo id, DirectorySettings dirSettings, Metadata md) {
            var fn = Path.GetFileNameWithoutExtension (id.FileName);
            if (!dateParser.Parse (fn)) return DateTime.MinValue;
            if (!dateParser.HasTime) {
               var dt = id.DateUtc.ToLocalTime ();
               if (dateParser.yy == dt.Year && dateParser.mm == dt.Month && dateParser.dd == dt.Day)
                  return id.DateUtc;
            }
            return dateParser.ToDate (DateTimeKind.Local).ToUniversalTime ();
         }
      }

      public class DirectoryNameDateFetcher : DateFetcher {
         private readonly DateParser2 dateParser;
         public DirectoryNameDateFetcher (string strSource, DateTime dt, bool assumeLocalInUnspecified)
            : base (strSource, dt, assumeLocalInUnspecified) {
            dateParser = new DateParser2 (DateParserFlags.AllowYearOnly | DateParserFlags.AllowYearMonth | DateParserFlags.DisallowTime | DateParserFlags.UseEndingNumber);
         }

         public override DateTime GetDate (IdInfo id, DirectorySettings dirSettings, Metadata md) {
            var fn = Path.GetDirectoryName (id.FileName);
            if (!dateParser.Parse (fn)) return DateTime.MinValue;
            return dateParser.ToDate (DateTimeKind.Local).ToUniversalTime ();
         }
      }
   }
}

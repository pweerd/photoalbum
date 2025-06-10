using Bitmanager.Core;
using Bitmanager.ImageTools;
using Bitmanager.Xml;
using System.Text.RegularExpressions;
using System.Xml;

namespace AlbumImporter {
   public enum _AlbumSource { None, FromFileName, FromDirectoryName, FromValue };
   public abstract class AlbumFetcher {
      public static readonly string DefaultFlags = "FromFileName,FromDirectoryName";
      public static readonly string DefaultSeps = ",;";
      public static readonly AlbumFetcher Default = Create (DefaultFlags, DefaultSeps, 3, null);

      protected readonly string Source, Seps, Value;
      protected readonly int MinAlbumLen;
      protected AlbumFetcher (string strSource, string seps, int minAlbumLen, string value) {
         Source = strSource;
         Seps = seps;
         Value = value.TrimToNull ();
         MinAlbumLen = minAlbumLen;
      }

      public abstract string GetAlbum (IdInfo id, DirectorySettings dirSettings, Metadata md);

      public override string ToString () {
         return Invariant.Format ("{0} [src={1}, seps=\"{2}\", value={3}, min_len={4}]",
            GetType().Name, Source, Seps, Value, MinAlbumLen);
      }

      public static AlbumFetcher Create (XmlNode node, AlbumFetcher def) {
         if (def == null) def = Default;
         if (node == null) return def;
         var val = node.ReadStr ("@value", def.Value);
         var src = node.ReadStr ("@src", def.Source);
         var minLen = node.ReadInt ("@min_len", def.MinAlbumLen);
         var seps = node.ReadStrRaw ("@seps", _XmlRawMode.Trim | _XmlRawMode.DefaultOnNull, def.Seps);
         node.CheckInvalidAttributes ("src", "value", "seps", "min_len");
         return AlbumFetcher.Create (src, seps, minLen, val);
      }

      public static AlbumFetcher Create (string strFlags, string seps, int minAlbumLen, string value) {
         var sepArr = string.IsNullOrEmpty (seps) ? null : seps.ToCharArray ();
         if (minAlbumLen < 0) minAlbumLen = 3;
         var list = new List<AlbumFetcher> ();
         string[] flags = strFlags.SplitStandard ();
         if (flags.Length == 0) return createOne (_AlbumSource.FromFileName, strFlags, seps, minAlbumLen, null);

         AlbumFetcher[] fetchers = new AlbumFetcher[flags.Length];
         for (int i = 0; i < flags.Length; i++) {
            var src = Invariant.ToEnum<_AlbumSource> (flags[i]);
            fetchers[i] = createOne (src, strFlags, seps, minAlbumLen, value);
         }

         return fetchers.Length == 1 ? fetchers[0] : new MultiAlbumFetcher (fetchers, strFlags, seps, minAlbumLen, value);
      }

      public static AlbumFetcher createOne (_AlbumSource src, string strFlags, string seps, int minAlbumLen, string value) {

         switch (src) {
            case _AlbumSource.None: return new AlbumNoneFetcher (strFlags, seps, minAlbumLen, value);
            case _AlbumSource.FromFileName: return new AlbumFromFileNameFetcher (strFlags, seps, minAlbumLen, value);
            case _AlbumSource.FromDirectoryName: return new AlbumFromDirectoryNameFetcher (strFlags, seps, minAlbumLen, value);
            case _AlbumSource.FromValue: return new AlbumValueFetcher (strFlags, seps, minAlbumLen, value);
         }
         src.ThrowUnexpected ();
         return null;
      }
   }

   public class MultiAlbumFetcher : AlbumFetcher {
      private readonly AlbumFetcher[] fetchers;
      public MultiAlbumFetcher (AlbumFetcher[] list, string strFlags, string seps, int minAlbumLen, string value) 
         : base (strFlags, seps, minAlbumLen, value) {
         fetchers = list;
      }

      public override string GetAlbum (IdInfo id, DirectorySettings dirSettings, Metadata md) {
         for (int i = 0; i < fetchers.Length; i++) {
            var a = fetchers[i].GetAlbum (id, dirSettings, md);
            if (a != null) return a;
         }
         return null;
      }
   }


   public class AlbumNoneFetcher : AlbumFetcher {
      public AlbumNoneFetcher (string strFlags, string seps, int minAlbumLen, string value) 
         : base (strFlags, seps, minAlbumLen, value) {
      }

      public override string GetAlbum (IdInfo id, DirectorySettings dirSettings, Metadata md) {
         return null;
      }
   }

   public class AlbumValueFetcher : AlbumFetcher {
      private readonly string value;
      public AlbumValueFetcher (string strFlags, string seps, int minAlbumLen, string value) 
         : base (strFlags, seps, minAlbumLen, value) {
         this.value = value;
      }

      public override string GetAlbum (IdInfo id, DirectorySettings dirSettings, Metadata md) {
         return value;
      }
   }

   public abstract class AlbumFromNameFetcher : AlbumFetcher {
      protected static readonly Regex exprOrderedImg = new Regex (@"^(IMG|SAM|P|DSCF|SGDN|Afbeelding)[_\- ]*(\d+)", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
      protected static readonly Regex exprWhatsappImg = new Regex (@"^IMG[_\- ](\d{8})[_\- ]WA(\d+)$", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
      protected char[] AlbumSeps;
      protected int MinAlbumLength;
      protected AlbumFromNameFetcher (string strFlags, string seps, int minAlbumLen, string value) 
         :base(strFlags, seps, minAlbumLen, value) {
         AlbumSeps = seps==null ? null : seps.ToArray();
         if (AlbumSeps != null && AlbumSeps.Length == 0) AlbumSeps = null;
         MinAlbumLength = minAlbumLen;
      }

      protected int getLengthBeforeAlbumSep (string s) {
         int N = s.Length;
         if (AlbumSeps == null) return N;
         int idx = AlbumSeps.Length == 1 ? s.IndexOf (AlbumSeps[0]) : s.IndexOfAny (AlbumSeps);
         return idx < 0 ? N : idx;
      }

      protected string albumFromName(string s) {
         if (exprOrderedImg.IsMatch (s) || exprWhatsappImg.IsMatch(s)) return null;

         int end = getLengthBeforeAlbumSep(s);
         int start = Trim.TrimLeft(s, 0, end);
         while (end > start) {
            int tmp = Trim.TrimLeftNumbers (s, start, end);
            if (tmp == start) break;
            start = tmp;
         }

         //Trim the right side. Maybe ending with (xx)
         end = Trim.TrimRight (s, start, end);
         end = Trim.TrimRightNumbers (s, start, end);
         if (end > start && s[end - 1] == ')')
            end = Trim.TrimRightParenthesis (s, start, end);  

         while (end>start) {
            int tmp = Trim.TrimRightNumbers (s, start, end);
            if (tmp == end) break;
            end = tmp;
         }
         var len = end - start;
         return len >= MinAlbumLength ? s.Substring(start, len): null;
      }
   }

   public class AlbumFromFileNameFetcher : AlbumFromNameFetcher {
      public AlbumFromFileNameFetcher (string strFlags, string seps, int minAlbumLen, string value) 
         : base (strFlags, seps, minAlbumLen, value) { }

      public override string GetAlbum (IdInfo id, DirectorySettings dirSettings, Metadata md) {
         return albumFromName (Path.GetFileNameWithoutExtension (id.FileName));
      }
   }

   public class AlbumFromDirectoryNameFetcher : AlbumFromNameFetcher {
      public AlbumFromDirectoryNameFetcher (string strFlags, string seps, int minAlbumLen, string value) 
         : base (strFlags, seps, minAlbumLen, value) { }

      public override string GetAlbum (IdInfo id, DirectorySettings dirSettings, Metadata md) {
         var dir = dirSettings.RelName;
         while (!string.IsNullOrEmpty (dir)) {
            string fn = Path.GetFileName (dir);
            if (HasLetters (fn)) return fn;
            dir = Path.GetDirectoryName (dir);
         }
         return null;
      }

      private static bool HasLetters (string s) {
         for (int i = 0; i < s.Length; i++) {
            if (char.IsLetter (s[i])) return true;
         }
         return false;
      }

   }
}

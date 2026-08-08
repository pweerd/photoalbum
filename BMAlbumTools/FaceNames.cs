using Bitmanager.Core;
using System.Text;
using System.Text.RegularExpressions;

namespace Bitmanager.AlbumTools {

   public class FaceNames {
      public readonly FaceName[] Names;
      public readonly FaceName[] SortedNames;
      public readonly Dictionary<int, int[]> TwinsSet;
      public readonly int Count;
      private static int changeId;
      public readonly int ChangeId;

      public FaceNames (string fn) {
         ChangeId = changeId++;
         var twins = new List<string[]>();
         try {
            if (!File.Exists (fn)) {
               SortedNames = Names = Array.Empty<FaceName> ();
               Count = 0;
            } else {
               bool emptyLines = false;
               var list = new List<FaceName> ();
               int i = 0;
               foreach (string l in File.ReadLines (fn)) {
                  string line = l.Trim ();
                  if (line.Length == 0) {
                     emptyLines = true;
                     continue;
                  }
                  if (emptyLines) {
                     throw new BMException ("Empty lines are not allowed!\nYou can 'delete' a line by writing DELETED in it");
                  }
                  if (line == "DELETED") {
                     ++i;
                     continue;
                  }
                  int idx = line.IndexOf ("//");
                  if (idx >= 0) {
                     if (idx == 0) continue;
                     line = line.Substring (0, idx);
                  }
                  if (idx >= 0) {
                     if (idx == 0) continue;
                     line = line.Substring (0, idx);
                  }

                  if (line.StartsWith("_TWINS:")) {
                     var arr = line.Substring(7).SplitStandard();
                     if (arr != null && arr.Length > 0) twins.Add (arr);
                     continue;
                  }

                  list.Add (new FaceName (i, line.Trim ()));
                  i++;
               }
               Names = list.ToArray ();
               Count = Names.Length;
               if (Names.Length <= 1)
                  SortedNames = Names;
               else {
                  list.Sort ((a, b) => StringComparer.OrdinalIgnoreCase.Compare (a.Name, b.Name));
                  SortedNames = list.ToArray ();
               }
               if (twins.Count > 0) {
                  TwinsSet = new Dictionary<int, int[]> (Count);
                  for (int j=0; j<twins.Count; j++) {
                     administrateTwins(TwinsSet, twins[j]);
                  }
               }
            }
         } catch (Exception err) {
            throw new BMException ("Error while loading FaceNames: {0}\nFile: {1}.", err.Message, fn);
         }
      }

      private void administrateTwins (Dictionary<int, int[]> twinsSet, string[] names) {
         var ret = new int[names.Length];
         for (int i=0; i<names.Length; i++) {
            ret[i] = -1;
            for (int j=0; j<SortedNames.Length; j++) {
               if (SortedNames[j].Name.StartsWith (names[i], StringComparison.OrdinalIgnoreCase)) {
                  if (j+1 < SortedNames.Length && SortedNames[j+1].Name.StartsWith (names[i], StringComparison.OrdinalIgnoreCase)) {
                     throw new BMException ("Twin name is ambigious. Multiple names starting with [{0}].", names[i]);
                  }
                  ret[i] = SortedNames[j].Id;
               }
            }
            if (ret[i] < 0)
               throw new BMException ("Twin name not found. No names starting with [{0}].", names[i]);
         }

         for (int i = 0; i < ret.Length; i++) {
            if (!twinsSet.TryAdd (ret[i], ret))
               throw new BMException ("Multiple twin sets found for name [{0}].", NameById (ret[i]));
         }
      }

      public string NameById (int id) {
         if (id < 0) return "Unknown" + id;
         if (id < Names.Length) return Names[id].Name;
         Logs.ErrorLog.Log ("Invalid face-id: {0}, count={1}, stack=\n{2}", id, Names.Length, Environment.StackTrace);
         return null;
      }

      public bool AreTwins (int id1, int id2) {
         if (TwinsSet == null || !TwinsSet.TryGetValue (id1, out var twins)) return false;
         return twins.Contains (id2);
      }

      [Flags]
      public enum FindNameFlags {SortedById=1, SearchEqual=2, SearchPrefix=4, SearchContains=8, MustExcept=16 };
      public FaceName FindName(string name, FindNameFlags flags) {
         var names = (flags & FindNameFlags.SortedById) != 0 ? this.Names : SortedNames;

         if (string.IsNullOrEmpty (name)) goto NOT_FOUND;

         if ((flags & FindNameFlags.SearchEqual) != 0) {
            for (int i = 0; i < names.Length; i++) {
               if (string.Equals (name, names[i].Name, StringComparison.InvariantCultureIgnoreCase))
                  return names[i];
            }
         }

         if ((flags & FindNameFlags.SearchPrefix) != 0) {
            for (int i = 0; i < names.Length; i++) {
               if (names[i].Name.StartsWith (name, StringComparison.InvariantCultureIgnoreCase))
                  return names[i];
            }
         }

         Regex expr=null;
         if ((flags & FindNameFlags.SearchContains) != 0) {
            expr = createRegexForName(name);
            for (int i = 0; i < names.Length; i++) {
               if (expr.IsMatch (names[i].Name))
                  return names[i];
            }
         }

         var sb = new StringBuilder();
         var lcName = lcLettersAndDigits (sb, name);
         var lcNames = new string[names.Length];
         for (int i = 0; i < names.Length; i++) {
            lcNames[i] = lcLettersAndDigits (sb, names[i].Name);
         }

         if ((flags & FindNameFlags.SearchEqual) != 0) {
            for (int i = 0; i < names.Length; i++) {
               if (lcName.Equals (lcNames[i]))
                  return names[i];
            }
         }

         if ((flags & FindNameFlags.SearchPrefix) != 0) {
            for (int i = 0; i < names.Length; i++) {
               if (lcNames[i].StartsWith (lcName))
                  return names[i];
            }
         }

         if ((flags & FindNameFlags.SearchContains) != 0) {
            for (int i = 0; i < names.Length; i++) {
               if (expr.IsMatch (lcNames[i]))
                  return names[i];
            }
         }


      NOT_FOUND:
         if ((flags & FindNameFlags.MustExcept) != 0) throw new BMException ("Face named [{0}] not found.", name);
         return null;
      }

      private static string lcLettersAndDigits(StringBuilder sb, string x) {
         sb.Clear ();
         for (int i=0; i<x.Length; i++) {
            if (Char.IsLetterOrDigit (x[i])) sb.Append (Char.ToLowerInvariant(x[i]));
         }
         return sb.ToString ();
      }

      private static Regex createRegexForName (string name) {
         var nameForExpr = name;
         if (name.Contains (' ')) {
            var sb = new StringBuilder();
            for (int i = 0; i < name.Length; i++) {
               if (name[i] == ' ') {
                  if (i > 0 && name[i - 1] == ' ') continue;
                  sb.Append (".*");
                  continue;
               }
               sb.Append (name[i]);
            }
            nameForExpr = sb.ToString ();
         }
         return new Regex(nameForExpr, RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
      }
   }


   public class FaceName {
      public readonly int Id;
      public readonly string Name;
      public FaceName (int id, string name) {
         Id = id;
         Name = name;
      }
   }
}

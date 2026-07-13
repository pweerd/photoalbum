using Bitmanager.Core;

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

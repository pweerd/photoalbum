using Bitmanager.Core;

namespace Bitmanager.AlbumTools {

   public class FaceNames {
      public readonly FaceName[] Names;
      public readonly FaceName[] SortedNames;
      public readonly int Count;
      private static int changeId;
      public readonly int ChangeId;

      public FaceNames (string fn) {
         ChangeId = changeId++;
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
            }
         } catch (Exception err) {
            throw new BMException ("Error while loading FaceNames: {0}\nFile: {1}.", err.Message, fn);
         }
      }

      public string NameById (int id) => Names[id].Name;
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

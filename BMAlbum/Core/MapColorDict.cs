using Bitmanager.Core;
using Bitmanager.Json;
using System;

namespace BMAlbum {
   public class MapColorDict {
      private const int USED = 0x40000000;
      private readonly MapSettings settings;
      private readonly bool[] inUse;
      private Dictionary<string, int> colorIndexes;
      private int curr;
      public MapColorDict (MapSettings settings) {
         Logs.DebugLog.Log ("Create colorDict");
         this.settings = settings;
         curr = settings.OtherPins.Length - 1;
         inUse = new bool[settings.OtherPins.Length];
         colorIndexes = new Dictionary<string, int>();
      }

      public void SetColorIndex (string album, int index) {
         Logs.DebugLog.Log ("-- SetColorIndex ({0}, {1})", album, index);
         if (album != null) {
            colorIndexes[album.ToLowerInvariant ()] = index;
            inUse[index] = true;
         }
      }

      public int GetColorIndex (string album) {
         if (album == null) album = string.Empty;
         album = album.ToLowerInvariant ();
         if (colorIndexes.TryGetValue (album, out var ret)) {
            if ((ret & USED)==0) colorIndexes[album] = ret | USED;
            return ret & ~USED;
         }

         int N = inUse.Length;
         for (int i=1; i<N; i++) { //loop 1 less, since otherwise we assign the same value over and over
            curr = (curr + 1) % N;
            if (!inUse[curr]) {
               inUse[curr] = true;
               break;
            }
         }
         colorIndexes[album] = curr | USED;
         Logs.DebugLog.Log ("-- GetColorIndex ({0}) -> {1})", album, curr);
         return curr;
      }

      public void ExportToJson (JsonWriter wtr) {
         wtr.WriteStartObject ("colors");
         foreach (var kvp in colorIndexes) {
            if ((kvp.Value & USED)==0) continue;
            wtr.WriteProperty(kvp.Key, kvp.Value & ~USED);
            Logs.DebugLog.Log ("-- export {0} -> {1})", kvp.Key, kvp.Value & ~USED);
         }
         wtr.WriteEndObject ();
      }
   }
}

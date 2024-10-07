using Bitmanager.Xml;
using System.Xml;

namespace BMAlbum {
   public class MapSettings {
      public readonly string GoogleKey;
      public readonly bool Enabled;
      public MapSettings (XmlNode node) {
         if (node != null) {
            Enabled = node.ReadBool ("@enabled", true);
            GoogleKey = node.ReadStr ("google/@key");
         }
      }
   }
}

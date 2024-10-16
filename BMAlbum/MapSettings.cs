using Bitmanager.Core;
using Bitmanager.Json;
using Bitmanager.Xml;
using System.Xml;

namespace BMAlbum {
   public class MapSettings {
      public readonly string GoogleKey;
      public readonly string PinSearchDistance;
      public readonly string StartPosition;
      public readonly string GroupPin;
      public readonly string SelectedPin;
      public readonly string[] OtherPins;
      private JsonObjectValue _json;
      public readonly int StartZoom;
      public readonly bool Enabled;
      public MapSettings (XmlNode node, string imagesDir) {
         if (node != null) {
            Enabled = node.ReadBool ("@enabled", true);
            GoogleKey = node.ReadStr ("google/@key");
            PinSearchDistance = node.ReadStr ("pins/@search_distance");
            GroupPin = node.ReadStr ("pins/group/@pin");
            SelectedPin = node.ReadStr ("pins/selected/@pin");
            StartPosition = node.ReadStr ("start/@center");
            StartZoom = node.ReadInt ("start/@zoom");

            var list = new List<string> ();
            var incl = node.ReadStr ("pins/other/@pin");
            if (incl.IndexOf ('*') < 0 && incl.IndexOf ('?') < 0) {
               OtherPins = incl.SplitStandard ();
            } else {
               foreach (var fn in Directory.GetFiles (imagesDir, incl)) {
                  var name = Path.GetFileName (fn);
                  if (name == GroupPin) continue;
                  if (name == SelectedPin) continue;
                  list.Add (name);
               }
               if (list.Count == 0)
                  throw new BMNodeException (node, "At least 1 pin is needed. Loaded 0 pins from {0}, incl={1}.", imagesDir, incl);
               OtherPins = list.ToArray ();
            }
         }
      }

      public JsonObjectValue ToJson () {
         if (_json == null) {
            var tmp = new JsonObjectValue ("key", GoogleKey);
            tmp.Add ("pin_search_distance", PinSearchDistance);
            tmp.Add ("start_position", StartPosition);
            tmp.Add ("start_zoom", StartZoom);
            tmp.Add ("group_pin", GroupPin);
            tmp.Add ("selected_pin", SelectedPin);
            tmp.Add ("other_pins", new JsonArrayValue (OtherPins));
            _json = tmp;
         }
         return _json;
      }
   }
}

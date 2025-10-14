using Bitmanager.Core;
using Bitmanager.Json;
using Bitmanager.Xml;
using BMAlbum.Core;
using System.Xml;
using System.Xml.Linq;

namespace BMAlbum {
   public class MapSettings {
      public readonly string GoogleKey;
      public readonly string PinSearchDistance;
      public readonly string StartPosition;
      public readonly string GroupPin;
      public readonly string SelectedPin;
      public readonly string[] OtherPins;
      public readonly PerDeviceSettings[] PerDeviceSettings;
      public readonly int StartZoom;
      public readonly int DetailZoom;
      public MapSettings (XmlNode node, string imagesDir) {
         if (node != null) {
            GoogleKey = node.ReadStr ("google/@key");
            PinSearchDistance = node.ReadStr ("pins/@search_distance");
            GroupPin = node.ReadStr ("pins/group/@pin");
            SelectedPin = node.ReadStr ("pins/selected/@pin");
            StartPosition = node.ReadStr ("start/@center");
            StartZoom = node.ReadInt ("start/@zoom");
            DetailZoom = node.ReadInt ("detail/@zoom", 15);
            PerDeviceSettings = createPerDeviceSettings (node);

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

      private static PerDeviceSettings[] createPerDeviceSettings (XmlNode mainNode) {
         var list = mainNode.SelectNodes ("per_device_settings/settings");
         PerDeviceSettings[] ret;
         if (list.Count == 0) {
            ret = new PerDeviceSettings[1];
            ret[0] = new PerDeviceSettings (BrowserType.All, new GpsSettings (null), new CompassSettings (null));
            return ret;
         }
         ret = new PerDeviceSettings[list.Count];
         for (int i = 0; i < ret.Length; i++) {
            ret[i] = new PerDeviceSettings (list[i]);
         }
         int last = ret.Length - 1;
         if (ret[last].Type != BrowserType.All) throw new BMNodeException (list[last], "Last element needs device=all.");
         return ret;
      }

      private PerDeviceSettings getDeviceSettings (BrowserType type) {
         for (int i = 0; i < PerDeviceSettings.Length; i++) {
            if ((PerDeviceSettings[i].Type & type) != 0) return PerDeviceSettings[i];
         }
         throw new BMException ("Cannot get PerDeviceSettings for type=" + type);
      }

      public void WriteClientConfig(JsonWriter json, BrowserType type) {
         json.WriteStartObject ();
         json.WriteProperty ("key", GoogleKey);
         json.WriteProperty ("pin_search_distance", PinSearchDistance);
         json.WriteProperty ("start_position", StartPosition);
         json.WriteProperty ("start_zoom", StartZoom);
         json.WriteProperty ("detail_zoom", DetailZoom);
         json.WriteProperty ("group_pin", GroupPin);
         json.WriteProperty ("selected_pin", SelectedPin);
         json.WriteStartArray ("other_pins");
         foreach (var p in OtherPins) json.WriteValue (p);
         json.WriteEndArray ();
         getDeviceSettings (type).WriteJson (json);
         json.WriteEndObject ();
      }
   }

   public class PerDeviceSettings {
      public readonly BrowserType Type;
      public readonly GpsSettings Gps;
      public readonly CompassSettings Compass;
      public readonly bool ShowGotoMap;
      public readonly bool Active;

      public PerDeviceSettings (BrowserType type, GpsSettings gps, CompassSettings compass) {
         Type = type;
         Gps = gps;
         Compass = compass;
         ShowGotoMap = true;
         Active = true;
      }
      public PerDeviceSettings (XmlNode node) {
         Type = node.ReadEnum<BrowserType>("@device");
         Active = node.ReadBool ("@map_active", true);
         ShowGotoMap = Active && node.ReadBool ("@show_goto_map", true);
         Gps = new GpsSettings (node.SelectMandatoryNode("gps"));
         Compass = new CompassSettings (node.SelectMandatoryNode ("compass"));
      }

      public void WriteJson (JsonWriter json) {
         json.WriteProperty ("active", Active);
         json.WriteProperty ("show_goto_map", ShowGotoMap);
         Gps.WriteJson (json);
         Compass.WriteJson (json);
      }

   }
   public struct GpsSettings {
      private enum _Granularity { fine, coarse};
      public readonly bool Active;
      public readonly bool Fine;
      public readonly bool Silent;
      public GpsSettings (XmlNode node) {
         if (node != null) {
            Active = node.ReadBool ("@active", true);
            Silent = node.ReadBool ("@silent", true);
            Fine = node.ReadEnum ("@granularity", _Granularity.coarse) == _Granularity.fine;
         }
      }

      public void WriteJson (JsonWriter json) {
         json.WriteStartObject ("gps");
         json.WriteProperty ("active", Active);
         json.WriteProperty ("silent", Silent);
         json.WriteProperty ("fine", Fine);
         json.WriteEndObject ();
      }

   }

   public struct CompassSettings {
      public readonly bool Active;
      public readonly bool Silent;
      public CompassSettings (XmlNode node) {
         if (node != null) {
            Active = node.ReadBool ("@active", true);
            Silent = node.ReadBool ("@silent", true);
         }
      }
      public void WriteJson (JsonWriter json) {
         json.WriteStartObject ("compass");
         json.WriteProperty ("active", Active);
         json.WriteProperty ("silent", Silent);
         json.WriteEndObject ();
      }
   }
}

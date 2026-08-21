using Bitmanager.Xml;
using System.Xml;
using System.Xml.Linq;

namespace BMAlbum {
   public class UploadSettings {
      public readonly string UploadRoot;
      public readonly string Filter;
      public readonly string RemoveText;
      public readonly string UploadText;
      public readonly int MaxMB;
      public readonly int MaxMBPerFile;
      public readonly int MaxFiles;
      public bool Active;

      public UploadSettings (XmlNode node) {
         Filter = node.ReadStr ("@filter", ".png,.jpg,.jpeg,.mp4,.mov,.heic,.heif");
         RemoveText = node.ReadStr ("@remove_text", null);
         UploadText = node.ReadStr ("@upload_text", "Klik hier om uw files te uploaden (of sleep uw files in deze box)");
         MaxFiles = node.ReadInt ("@max_files", 100);
         MaxMB = node.ReadInt ("@max_mb", 500);
         MaxMBPerFile = node.ReadInt ("@max_mb_per_file", 50);
         UploadRoot = node.ReadStr ("@root", "upload"); 
         Active = node.ReadBool ("@active", true);
      }
   }
}

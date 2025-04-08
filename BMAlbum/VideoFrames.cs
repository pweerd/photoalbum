using Bitmanager.IO;
using Bitmanager.Storage;
using Bitmanager.Web;
using Bitmanager.Xml;
using BMAlbum.Core;
using System.Xml;
using System.Xml.Linq;

namespace BMAlbum {
   public class VideoFrames {
      private readonly FileStorage frames;
      private readonly FileGenerations2 gens;
      public string Filename => frames.FileName;

      public static VideoFrames Create (XmlNode node) {
         string dir = node.ReadPath ("@dir");
         FileGenerations2 gens = new FileGenerations2 (Path.Combine (dir, "video_frames"), ".stor");
         string fn = gens.Target;
         WebGlobals.Instance.SiteLog.Log ("Video frames using {0}", fn);
         if (fn != null) {
            return new VideoFrames(gens, new FileStorage (fn, FileOpenMode.Read));
         }
         return null;
      }

      private VideoFrames(FileGenerations2 gen, FileStorage frames) {
         this.gens = gen;
         this.frames = frames;
      }

      public byte[] GetFrame (string id) {
         return FileStorageAccessor.GetBytes (frames, frames.GetFileEntry (id));
      }
   }
}

using Bitmanager.Core;
using Bitmanager.IO;
using Bitmanager.Storage;
using Bitmanager.Web;
using Bitmanager.Xml;
using BMAlbum.Core;
using System.Xml;
using System.Xml.Linq;

namespace BMAlbum {
   public class VideoFrames {
      private FileStorage frames;
      private readonly FileGenerations2 gens;
      public string Filename => frames.FileName;

      public static VideoFrames Create (XmlNode node) {
         string dir = node.ReadPath ("@dir");
         FileGenerations2 gens = new FileGenerations2 (Path.Combine (dir, "video_frames"), ".stor");
         string fn = gens.Target;
         var g = WebGlobals.Instance;
         g.SiteLog.Log ("Video frames using {0}", fn);
         if (!g.GlobalChangeRepository.ContainsKey (Events.EV_VIDEO_FRAMES_CHANGED)) {
            g.GlobalChangeRepository.RegisterFileWatcher (dir,
                                        false,
                                        new NameFilter ("video_frames($|.+\\.stor$)", true),
                                        ChangeType.Changed | ChangeType.Renamed,
                                        Events.EV_VIDEO_FRAMES_CHANGED,
                                        true);
            g.GlobalChangeRepository.RegisterChangeHandler (onChange);
         }
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

      public List<FileEntry> GetFrameEntries() {
         return frames.Entries.ToList ();
      }

      private static void onChange (string key, object context) {
         if (key != Events.EV_VIDEO_FRAMES_CHANGED) return;

         string fn = context as string;
         if (fn == null) return;

         var g = WebGlobals.Instance;
         var videoFrames = ((Settings)g.Settings).VideoFrames;
         var frames = videoFrames.frames;

         if (string.Equals (fn, frames.FileName, StringComparison.OrdinalIgnoreCase) ||
             string.Equals (Path.GetFileName (fn), "video_frames", StringComparison.OrdinalIgnoreCase)) {

            try {
               fn = videoFrames.gens.Target;
               var oldFrames = frames;
               videoFrames.frames = new FileStorage (fn, FileOpenMode.Read);

               g.DelayedDisposer.Add(oldFrames);

               g.SiteLog.Log (_LogType.ltInformational, "Reloaded video frames using {0}", fn);
               g.GlobalChangeRepository.FireChangeEvent (Events.EV_VIDEO_FRAMES_RELOADED, videoFrames.frames);
            } catch (Exception e) {
               string msg = "Error while updating video frames: " + e.Message;
               Logs.ErrorLog.Log (e, msg);
               g.SiteLog.Log (_LogType.ltError, msg);
            }
         }
      }

   }
}

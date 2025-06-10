using Bitmanager.Core;
using Bitmanager.IO;
using Bitmanager.Storage;
using Bitmanager.Web;
using Bitmanager.Xml;
using BMAlbum.Core;
using Microsoft.AspNetCore.SignalR.Protocol;
using System.Xml;
using System.Xml.Linq;

namespace BMAlbum {
   public class VideoFrames {
      private FileStorage frames;
      private readonly FileGenerations2 gens;
      public string Filename => frames?.FileName;

      public static VideoFrames Create (XmlNode node) {
         string dir = node.ReadPath ("@dir");
         FileGenerations2 gens = new FileGenerations2 (Path.Combine (dir, "video_frames"), ".stor");
         string fn = gens.Target;
         var g = WebGlobals.Instance;
         g.SiteLog.Log ("Loading video frames from dir [{0}]. Current file=[{1}].", dir, fn==null ? null : Path.GetFileName(fn));
         if (!g.GlobalChangeRepository.ContainsKey (Events.EV_VIDEO_FRAMES_CHANGED)) {
            //g.GlobalChangeRepository.Debug = true;
            g.GlobalChangeRepository.RegisterFileWatcher (dir,
                                        false,
                                        new NameFilter ("video_frames($|.+\\.stor$)", true),
                                        ChangeType.Changed | ChangeType.Renamed,
                                        Events.EV_VIDEO_FRAMES_CHANGED,
                                        true);
            g.GlobalChangeRepository.RegisterChangeHandler (onChange);
         }

         return new VideoFrames (gens, fn==null ? null : new FileStorage (fn, FileOpenMode.Read));
      }

      private VideoFrames(FileGenerations2 gen, FileStorage frames) {
         this.gens = gen;
         this.frames = frames;
      }

      public byte[] GetFrame (string id, bool raiseException=true) {
         if (frames == null) goto NOT_FOUND;
         var bytes = FileStorageAccessor.GetBytes (frames, frames.GetFileEntry (id));
         if (bytes != null) return bytes;

         NOT_FOUND:
         if (raiseException) {
            string err;
            if (frames == null) err = "No videoFrames loaded.";
            else err = Invariant.Format ("ID [{0}] not found in [{1}].", id, frames.FileName);
            throw new BMException (err);
         }
         return null;
      }

      public List<FileEntry> GetFrameEntries() {
         if (frames == null) return new List<FileEntry>(0);
         return frames.Entries.ToList ();
      }

      private static void onChange (string key, object context) {
         if (key != Events.EV_VIDEO_FRAMES_CHANGED) return;

         string fn = context as string;
         if (fn == null) return;

         var g = WebGlobals.Instance;
         var videoFrames = ((Settings)g.Settings).VideoFrames;
         var frames = videoFrames.frames;
         var currentFn = frames?.FileName;

         if (string.Equals (fn, currentFn, StringComparison.OrdinalIgnoreCase) ||
             string.Equals (Path.GetFileName (fn), "video_frames", StringComparison.OrdinalIgnoreCase)) {

            try {
               fn = videoFrames.gens.GetRefreshedTarget();
               g.SiteLog.Log ("Reloading video frames from [{0}].", fn);
               var oldFrames = frames;
               videoFrames.frames = fn==null ? null : new FileStorage (fn, FileOpenMode.Read);
               if (oldFrames != null) g.DelayedDisposer.Add(oldFrames);

               g.SiteLog.Log (_LogType.ltInformational, "Reloaded video frames from {0}", fn);
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

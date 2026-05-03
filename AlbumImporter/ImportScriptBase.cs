/*
 * Copyright © 2023, De Bitmanager
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *    http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using Bitmanager.AlbumTools;
using Bitmanager.Core;
using Bitmanager.Elastic;
using Bitmanager.ImportPipeline;
using Bitmanager.IO;
using Bitmanager.Json;
using Bitmanager.Storage;
using Bitmanager.Xml;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System;

namespace AlbumImporter {


   /// <summary>
   /// Base class for the ImportScripts
   /// </summary>
   public class ImportScriptBase : IDisposable {
      protected IdInfo idInfo;
      protected Logger logger;
      protected IndexInfo curIndex, oldIndex, copyFromIndex, activeOldIndex;
      protected string faceAdminDir;
      protected string videoFramesDir;
      protected string tempFrameFile;
      protected FileGenerations2 videoFramesGeneration;
      protected FileStorage videoFrames;


      protected ESConnection esConnection;

      protected int sleepAfterExtract;
      protected int maxErrors;
      protected bool fullImport;
      protected bool forceRebuild;
      protected bool handleExceptions;
      protected bool sameIndexOrNotExist;  //curIndex == activeOldIndex || activeOldIndex==null

      protected ImportScriptBase () {
         logger = Logs.ErrorLog;
         maxErrors = 0;
      }

      public object OnError (PipelineContext ctx, object value) {
         if (!handleExceptions || --maxErrors < 0) {
            ctx.ActionFlags &= ~_ActionFlags.Handled;
            return null;
         }
         var e = (Exception)value;
         logger.Log (_LogType.ltError, "Error while processing {0}: {1}.", idInfo, e.GetBestMessage ());
         Logs.ErrorLog.Log (e, "Error while processing {0}: {1}.", idInfo, e.GetBestMessage ());

         return null;
      }

      protected void WaitAfterExtract() {
         if (sleepAfterExtract>0) Thread.Sleep (sleepAfterExtract);
      }

      protected void Init(PipelineContext ctx, bool exceptIfNotESEndpoint, int maxErrors=50, bool initVideoFrames=true ) {
         logger = ctx.ImportLog;
         this.handleExceptions = false;
         this.maxErrors = maxErrors;
         fullImport = (ctx.ImportFlags & _ImportFlags.FullImport) != 0;
         forceRebuild = (ctx.ImportFlags & _ImportFlags.ForceRebuild) != 0;
         faceAdminDir = XmlUtils.ReadPath (ctx.ImportEngine.Xml.DocumentElement, "faces_admin/@dir", null);
         videoFramesDir = XmlUtils.ReadPath (ctx.ImportEngine.Xml.DocumentElement, "video_frames/@dir", null);
         tempFrameFile = Path.Combine (ctx.ImportEngine.TempDir, "tmp_frame.jpg");

         var dsNode = ctx.DatasourceAdmin.ContextNode;
         sleepAfterExtract = ctx.DatasourceAdmin.ContextNode.ReadInt ("@sleep_after_extract", 0);
         copyFromIndex = IndexInfo.Create(dsNode.ReadStr("copy_from/@url", null), true);

         videoFramesGeneration = new FileGenerations2 (Path.Combine (videoFramesDir, "video_frames"), ".stor");
         if (initVideoFrames) {
            string fn = videoFramesGeneration.Target;
            if (fn != null) videoFrames = new FileStorage (fn, FileOpenMode.Read);
         }

         var ep = ctx.Action.Endpoint as ESDataEndpoint;
         if (ep == null) {
            if (exceptIfNotESEndpoint) throw new BMException ("Endpoint is not an ES endpoint but [{0}].", ctx.Action.Endpoint?.GetType ().FullName);
            return;
         }

         //Determine curIndex, oldIndex, etc
         esConnection = ep.Connection;
         curIndex = IndexInfo.Create(ep, 'N');
         oldIndex = IndexInfo.Create(ep, 'O');
         activeOldIndex = oldIndex;
         if (fullImport) {
            if (copyFromIndex != null) activeOldIndex = copyFromIndex;
         } else {
            if (copyFromIndex != null)
               logger.Log (_LogType.ltWarning, "Ignored copy_from [{0}]. copy_from is only supported in fullindex-mode.", copyFromIndex.Url);
            copyFromIndex = null;
         }
         sameIndexOrNotExist = activeOldIndex == null || curIndex.IsSameIndex (activeOldIndex);

         curIndex.Refresh();
         activeOldIndex?.Refresh();

         logger.Log (_LogType.ltInfo, "Indexes: cur={0}, Old={1}, Copyfrom={2}, ActiveOld={3}, sameIndexOrNotExist={4}",
            curIndex, oldIndex, copyFromIndex, activeOldIndex, sameIndexOrNotExist);
      }

      protected FaceNames ReadFaceNames () {
         return new FaceNames (Path.Combine (faceAdminDir, "facenames.txt"));
      }


      /// <summary>
      /// Helper function to return the filename for
      /// - the photo (if photo)
      /// - the extracted frame  (if video)
      /// Note that the frame file is a temp. one: it will be overwritten for each video
      /// </summary>
      protected string getItemFileName () {
         if (idInfo.MimeType.StartsWith ("video")) {
            //For video's: Store the frame into the tempfile and return the tempfile name.
            byte[] bytes = videoFrames.GetBytes (idInfo.Id, true);
            File.WriteAllBytes (tempFrameFile, bytes);
            return tempFrameFile;
         }
         return idInfo.FileName;
      }

      /// <summary>
      /// Helper function to return the loaded image for
      /// - the photo (if photo)
      /// - the extracted frame  (if video)
      /// </summary>
      protected Image<Rgb24> getItemImage () {
         if (idInfo.MimeType.StartsWith ("video")) {
            //For video's: get the image from the storage file.
            byte[] bytes = videoFrames.GetBytes (idInfo.Id, true);
            return Image.Load<Rgb24> (bytes);
         }
         return Image.Load<Rgb24> (idInfo.FileName);

      }


      public virtual void Dispose () {
         videoFrames?.Dispose ();
      }
   }
}

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

using AlbumImporter.FaceRecognition;
using Bitmanager.AlbumTools;
using Bitmanager.Core;
using Bitmanager.ImportPipeline;
using Bitmanager.Storage;
using Bitmanager.Xml;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Diagnostics;
using Rectangle = SixLabors.ImageSharp.Rectangle;
using RectangleF = SixLabors.ImageSharp.RectangleF;

namespace AlbumImporter {
   public class ImportScript_FaceExtract : ImportScriptBase {
      static readonly JpegEncoder jpgEncoder = new JpegEncoder () { Quality = 92 };

      private FaceCollection existingFaces;
      private readonly FaceAiHelper hlp;
      private FaceNames faceNames;
      private readonly MemoryStream mem;
      private Storages storages;
      private FaceStatistics faceStats;
      private int lastFaceStorageId;

      private int rotateMaxFaceCount;
      private float rotateBestFactor;

      public ImportScript_FaceExtract () {
         Configuration.Default.MaxDegreeOfParallelism = 1;
         hlp = new FaceAiHelper ();
         mem = new MemoryStream ();
         rotateMaxFaceCount = 4;
         rotateBestFactor = 1.25f;
      }

      public object OnDatasourceStart (PipelineContext ctx , object value) {
         Init (ctx , true);

         var dsNode = ctx.DatasourceAdmin.ContextNode;
         rotateMaxFaceCount = dsNode.ReadInt ("face_rotate/@max_face_count" , 4);
         rotateBestFactor = (float)dsNode.ReadFloat ("face_rotate/@best_factor" , 1.09f);


         faceNames = ReadFaceNames ();
         existingFaces = new FaceCollection ();
         existingFaces.Load (
            ctx.ImportLog,
            activeOldIndex,
            !sameIndex
         );

         faceStats = new FaceStatistics (logger, faceNames, ctx.ImportEngine.Xml.BaseDir);
         if (sameIndex) {
            // we keep the assigned storId's and save the old stats
            lastFaceStorageId = existingFaces.LargestStorageId;
            faceStats.LoadExisting (activeOldIndex, true);
         }

         logger.Log ("Loading/Creating bitmap storage");
         if (sameIndex)
            storages = new Storages (faceAdminDir, curIndex.Timestamp);
         else
            storages = new Storages (faceAdminDir, curIndex.Timestamp, oldIndex?.Timestamp);

         ctx.ImportLog.Log ("Starting faces extract. Flags={0}, existing records={1}, cur={2}, old={3}, copy={4}" ,
            ctx.ImportFlags ,
            existingFaces.Count ,
            curIndex ,
            oldIndex ,
            copyFromIndex
         );
         ctx.ImportLog.Log ("Face rotation parms: max_face_count={0}, best_factor={1}" , rotateMaxFaceCount , rotateBestFactor);

         handleExceptions = true;
         return null;
      }


      //For testing
      public Storages InitStorages (string dir) {
         return storages = new Storages (dir , "new" , "old");
      }


      public object OnDatasourceEnd (PipelineContext ctx , object value) {
         var logger = ctx.ImportLog;
         handleExceptions = false;

         var ep = (ESDataEndpoint)ctx.Action.Endpoint;
         ep.FlushCache ();
         curIndex.Refresh ();

         logger.Log ("Closing storage files");
         storages?.Dispose ();

         try {
            var syncher = new StorageSyncher (ctx.ImportLog, curIndex);
            syncher.Synchronize (faceAdminDir);
         } catch (Exception e) {
            ctx.ImportLog.Log (e , "Failed to synchronize: {0}" , e.Message);
         }

         if (sameIndex) {
            faceStats.DumpNameUsage (curIndex);
            faceStats.DumpDifferences (curIndex);
         } else {
            faceStats.DumpNameUsage (curIndex, activeOldIndex, true);
            faceStats.DumpDifferences (curIndex, activeOldIndex, true);
         }

         return null;
      }

      public object OnId (PipelineContext ctx, object value) {
         idInfo = (IdInfo)value;

         //if (idInfo.Id.StartsWith(@"D\2009-02-22 Jan en Jeannet 25 jaar\20090222 152351  Jan en Jeannet 25 jaar.JPG"))
         //   Debugger.Break ();
         List<DbFace> faces=null;
         List<DbFace> existing = FaceCollection.GetExistingFacesForId (existingFaces.GetFaces (), idInfo.Id);
         if (existing != null) {
            faces = existing;
            if (!forceRebuild || !fullImport) goto EXPORT;
         }

      EXTRACT:
         faces = extractFaces (idInfo);
         if (faces[0].FaceCount > 0) {
            if (existing != null) {
               combineExistingFaces (faces, existing);
            }
         }
         WaitAfterExtract ();

      EXPORT:
         exportFaces (ctx, faces); //export faces that need to be exported
         return null;
      }



      //Match the new extracted face from the existing faces (extracted a previous time)
      //This is done by selecting the most overlapping rectangles (as stored in relpos)
      private void combineExistingFaces (List<DbFace> curFaces , List<DbFace> existing) {
         //We loop backwards, since we remove entries from the existing faces
         //and the lists are most probable equal sorted. Meaning that if we reverse loop,
         //the last entries of existing will be removed first.
         for (int i = curFaces.Count; i > 0; ) {
            var curFace = curFaces[--i];
            int best = curFace.FindMostOverlapping (existing);
            if (best < 0) continue;

            var bestExistingFace = existing[best];
            existing.RemoveAt (best);
            curFace.NameSrc = bestExistingFace.NameSrc;
            curFace.Names = bestExistingFace.Names;
            curFace.Updated = bestExistingFace.Updated;

            //Copy the storageId for non-extracted (to be copied) faces
            //This will be used in exportFaces()
            if (curFace.FaceStorageId < 0)
               curFace.FaceStorageId = bestExistingFace.FaceStorageId;
         }
      }

      private void exportFaces (PipelineContext ctx, List<DbFace> list) {
         var ep = ctx.Action.Endpoint;
         int exported = 0;
         for (int i = 0; i < list.Count; i++) {
            var face = list[i];
            face.User = idInfo.User;

            //Copy face-image and embeddings if needed and not yet done (by extractor)
            //NB: the assigned face.StorageID is the old existing storId 
            //    so we have to replace it
            if (!sameIndex && face.Embeddings==null && face.FaceStorageId >= 0) {
               int newStorId = ++lastFaceStorageId;
               storages.CopyOldToCur (face.FaceStorageId.ToString (), newStorId.ToString ());
               face.FaceStorageId = newStorId;
            }

            face.UpdateNames (faceNames);
            if (face.CopyNeeded) {
               face.Export (ep.Record);
               ctx.Pipeline.HandleValue (ctx, "record/face", face);
               ++exported;
            }
         }
         ctx.Skipped += list.Count - exported;
      }

      private static Image<Rgb24> extract (Image<Rgb24> srcImage , RectangleF srcRect) {
         int height = (int)srcRect.Height;
         Image<Rgb24> dstImage = new ((int)srcRect.Width, height);

         srcImage.ProcessPixelRows (dstImage , (srcAccessor , dstAccessor) => {
            for (int i = 0; i < height; i++) {
               Span<Rgb24> srcRow = srcAccessor.GetRowSpan ((int)srcRect.Y + i);
               Span<Rgb24> dstRow = dstAccessor.GetRowSpan (i);

               srcRow.Slice ((int)srcRect.X , (int)srcRect.Width).CopyTo (dstRow);
            }
         });
         return dstImage;
      }

      private static RectangleF createLargerFaceRect (in RectangleF rc , int maxW , int maxH) {
         int deltaX = Math.Max (20, (int)(.4f * rc.Width));
         int deltaY = Math.Max (20, (int)(.3f * rc.Height));
         var left = roundDown (rc.X, deltaX);
         var top = roundDown (rc.Y, deltaY);
         var right = roundUp (rc.Right, deltaX, maxW);
         var bot = roundUp (rc.Bottom, deltaY, maxH);
         var w = right - left;
         var h = bot - top;
         int diff = (w - h) / 2;
         if (diff > 0) {
            top -= diff;
            bot += diff;
            if (top < 0) top = 0;
            if (bot > maxH) bot = maxH;
            h = bot - top;
         } else if (diff < 0) {
            left += diff;
            right -= diff;
            if (left < 0) left = 0;
            if (right > maxW) right = maxW;
            w = right - left;
         }
         return new RectangleF (left , top , w , h);
      }

      public List<DbFace> extractFaces (IdInfo idInfo) {
         string idPrefix = idInfo.Id + "~";
         var dbFaces = new List<DbFace> ();
         Image<Rgb24> img = null;
         Image<Rgb24> imgFace = null;
         const int MAX = 250;
         try {
            img = getItemImage ();
            hlp.RotateBasedOnExifValue (img);

            var detResults = hlp.DetectFaces (img);
            if (detResults == null) {
               dbFaces.Add (new DbFace (idPrefix + "0"));
               return dbFaces;
            }

            bool doRotateCheck = detResults.Length <= rotateMaxFaceCount;
            DbFace dbFace;
            int imgW = img.Width;
            int imgH = img.Height;
            for (int i = detResults.Length; i > 0;) {
               var detResult = detResults[--i];
               var largerRect = createLargerFaceRect (detResult.Box, imgW, imgH);
               imgFace?.Dispose ();
               imgFace = extract (img , largerRect);

               //Downscale if needed
               int w = imgFace.Width;
               int h = imgFace.Height;
               float scaleFactor  = Math.Min(MAX / (float)w, MAX / (float)h);
               if (scaleFactor < 1f) {
                  imgFace.Mutate (x => x.Resize ((int)(w * scaleFactor) , (int)(h * scaleFactor) , KnownResamplers.Lanczos3));
               }

               RotateMode faceOrientation = RotateMode.None;
               bool faceOK = false;
               if (doRotateCheck) {
                  faceOK = hlp.RotateAndDetectFaces (ref imgFace , rotateBestFactor , out faceOrientation)?.Length > 0;
               }

               int storId = ++lastFaceStorageId;
               string storKey = storId.ToString ();

               mem.SetLength (0);
               imgFace.SaveAsJpeg (mem , jpgEncoder);
               storages.CurrentFaceStorage.AddBytes (mem.GetBuffer () , 0 , (int)mem.Length , storKey , DateTime.UtcNow , EntryFlags.None);
               if (!faceOK && !doRotateCheck) {
                  imgFace.Dispose ();
                  mem.Position = 0;
                  imgFace = Image.Load<Rgb24> (mem);
                  faceOK = hlp.HasFace (imgFace);
               }

               dbFace = new DbFace ();
               dbFace.FaceCount = detResults.Length;
               dbFace.FaceStorageId = storId;
               dbFace.W0 = imgFace.Width;
               dbFace.H0 = imgFace.Height;

               //Create and save the embeddings
               using (var cloned = img.Clone ()) {
                  hlp.Align (cloned, detResult);
                  dbFace.Embeddings = hlp.CreateEmbedding (cloned);
               }
               var bytes = BufferHelper.ToByteArray (dbFace.Embeddings);
               storages.CurrentEmbeddingStorage.AddBytes (bytes , 0 , bytes.Length , storKey , DateTime.UtcNow , EntryFlags.Deflate);



               //pw if (rotMode != RotateMode.None) largerRect.Rotate (imgW, imgH, rotMode.ToBackwardRotate ());
               float div = imgW > imgH ? imgW : imgH;
               dbFace.RelPos = new RelPos(largerRect.Left / div, largerRect.Right / div, largerRect.Top / div, largerRect.Bottom / div);
               dbFace.FaceRatio = detResult.GetFaceRatio ();
               dbFace.FaceAngleRaw = detResult.GetFaceAngle ();
               dbFace.FaceAngle = dbFace.FaceAngleRaw < 0 ? -1 : ((dbFace.FaceAngleRaw + 45) / 90) * 90;
               dbFace.FaceOrientation = faceOrientation;
               dbFace.FaceOK = faceOK;
               dbFaces.Add (dbFace);
            }

            //and assign IDs
            for (int i = 0; i < dbFaces.Count; i++) dbFaces[i].Id = idPrefix + i;
            return dbFaces;
         } finally {
            img?.Dispose ();
         }
      }

      private static int roundDown (float f , int delta) {
         var x = ((int)(f)) - delta;
         return x < 0 ? 0 : x;
      }
      private static int roundUp (float f , int delta , int max) {
         var x = ((int)(.5f + f)) + delta;
         return x > max ? max : x;
      }


   }
}

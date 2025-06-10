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
using Bitmanager.Storage;
using Bitmanager.Xml;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Buffers.Text;
using System.Diagnostics;
using Rectangle = SixLabors.ImageSharp.Rectangle;
using RectangleF = SixLabors.ImageSharp.RectangleF;

namespace AlbumImporter {
   public class ImportScript_FaceExtract : ImportScriptBase {
      static readonly JpegEncoder jpgEncoder = new JpegEncoder () { Quality = 92 };

      //We use PROCESSED as a mark that we processed the face from the prev db
      private static readonly float[] PROCESSED = new float[0];
      private FaceCollection existingFaces;
      private readonly FaceAiHelper hlp;
      private FaceNames faceNames;
      private PhotoFaceCounters counters, oldCounters;
      private readonly MemoryStream mem;
      private Storages storages;

      private HashSet<string> existingFaceRecIds;
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
      public object OnDatasourceStart (PipelineContext ctx, object value) {
         Init (ctx, true);

         var dsNode = ctx.DatasourceAdmin.ContextNode;
         rotateMaxFaceCount = dsNode.ReadInt ("face_rotate/@max_face_count", 4);
         rotateBestFactor = (float)dsNode.ReadFloat ("face_rotate/@best_factor", 1.09f);


         faceNames = ReadFaceNames ();
         counters = new PhotoFaceCounters ();
         oldCounters = new PhotoFaceCounters ();

         string url = base.copyFromUrl;
         if (url == null) url = base.oldIndexUrl;
         existingFaces = new FaceCollection (ctx.ImportLog, url, fullImport);
         existingFaceRecIds = new HashSet<string> ();
         //Hack: always load existing ids
         foreach (var f in existingFaces.GetFaces ()) existingFaceRecIds.Add (f.MainId);
         ctx.ImportLog.Log ("Existing known record-ids: {0}, from={1} ", existingFaceRecIds.Count, existingFaces.Count);


         //In case of a full import we reassign the ID's
         if (fullImport) {
            foreach (var f in existingFaces.GetFaces()) oldCounters.AddCounters (f);
            ctx.ImportLog.Log ("Dumping existing IDs");
            foreach (var s in existingFaces.GetFaces ().Select (f => f.Id).OrderBy (s => s)) {
               ctx.ImportLog.Log ("-- {0}", s);
            }
         } else {
            existingFaceRecIds = new HashSet<string> ();
            foreach (var f in existingFaces.GetFaces ()) existingFaceRecIds.Add(f.MainId);
            lastFaceStorageId = existingFaces.LargestStorageId;
         }
         ctx.ImportLog.Log ("FINGERPRINT: [{0}]", existingFaces.FingerPrint);

         logger.Log ("Loading/Creating bitmap storage");
         if (fullImport || oldIndex == null)
            storages = new Storages (faceAdminDir, newTimestamp, oldTimestamp);
         else
            storages = new Storages (faceAdminDir, newTimestamp);

         ctx.ImportLog.Log ("Starting faces extract. FullImport={0}, copy_from={1}, existing records={2}",
            fullImport,
            copyFromUrl,
            existingFaces.Count);
         ctx.ImportLog.Log ("Face rotation parms: max_face_count={0}, best_factor={1}", rotateMaxFaceCount, rotateBestFactor);

         handleExceptions = true;
         return null;
      }


      //For testing
      public Storages InitStorages (string dir) {
         return storages = new Storages (dir, "new", "old");
      }


      public object OnDatasourceEnd (PipelineContext ctx, object value) {
         var logger = ctx.ImportLog;
         handleExceptions = false;
         logger.Log ("Closing storage files");
         storages?.Dispose ();

         if (fullImport) {
            counters.LogDifferences (ctx.ImportLog, oldCounters);
         }

         try {
            if (esConnection != null && newIndex != null) {
               var syncher = new StorageSyncher (ctx.ImportLog, esConnection, newIndex);
               syncher.Synchronize (faceAdminDir);
            }
         } catch (Exception e) {
            ctx.ImportLog.Log (e, "Failed to synchronize: {0}", e.Message);
         }

         return null;
      }

      public object OnId (PipelineContext ctx, object value) {
         idInfo = (IdInfo)value;
         if (!File.Exists (idInfo.FileName)) {
            ctx.ActionFlags |= _ActionFlags.Skip;
            goto EXIT_RTN;
         }

         List<DbFace> faces;
         if (fullImport) {
            //!!!Hack to only import the existing faces!!!
            //if (!existingFaceRecIds.Contains (idInfo.Id)) {
            //   ctx.ActionFlags |= _ActionFlags.Skip;
            //   goto EXIT_RTN;
            //}

            //Full import
            faces = extractFaces (idInfo);
            if (faces[0].FaceCount > 0) {
               var existing = FaceCollection.GetExistingFacesForId (existingFaces.GetFaces (), idInfo.Id);
               if (existing != null) {
                  combineExistingFaces (faces, existing);
               }
            }
         } else {
            //Incr import
            if (existingFaceRecIds.Contains (idInfo.Id)) goto EXIT_RTN;
            faces = extractFaces (idInfo);
         }
         exportFaces (ctx, faces);
         //if (faces[0].FaceCount>0) 
            WaitAfterExtract ();

      EXIT_RTN:
         return null;
      }



      private void combineExistingFaces (List<DbFace> curFaces, List<DbFace> existing) {
         for (int i = 0; i < curFaces.Count; i++) {
            var curFace = curFaces[i];
            int best = curFace.FindMostOverlapping (existing);
            if (best < 0) continue;

            var bestExistingFace = existing[best];
            existing.RemoveAt (best);
            curFace.NameSrc = bestExistingFace.NameSrc;
            curFace.Names = bestExistingFace.Names;
            curFace.Updated = bestExistingFace.Updated; //PW misschien niet?
            bestExistingFace.Embeddings = PROCESSED; //Mark that we processed the face
            counters.AddCounters (curFace);
         }
      }

      private void exportFaces (PipelineContext ctx, List<DbFace> list) {
         var ep = ctx.Action.Endpoint;
         for (int i = 0; i < list.Count; i++) {
            var face = list[i];
            face.User = idInfo.User;
            face.UpdateNames (faceNames);
            face.Export (ep.Record);
            ctx.Pipeline.HandleValue (ctx, "record/face", face);
         }
      }


      private static Image<Rgb24> extract (Image<Rgb24> srcImage, RectangleF srcRect) {
         int height = (int)srcRect.Height;
         Image<Rgb24> dstImage = new ((int)srcRect.Width, height);

         srcImage.ProcessPixelRows (dstImage, (srcAccessor, dstAccessor) => {
            for (int i = 0; i < height; i++) {
               Span<Rgb24> srcRow = srcAccessor.GetRowSpan ((int)srcRect.Y + i);
               Span<Rgb24> dstRow = dstAccessor.GetRowSpan (i);

               srcRow.Slice ((int)srcRect.X, (int)srcRect.Width).CopyTo (dstRow);
            }
         });
         return dstImage;
      }

      private static RectangleF createLargerFaceRect(in RectangleF rc, int maxW, int maxH) {
         int deltaX = Math.Max (20, (int)(.4f * rc.Width));
         int deltaY = Math.Max (20, (int)(.3f * rc.Height));
         var left = roundDown (rc.X, deltaX);
         var top = roundDown (rc.Y, deltaY);
         var right = roundUp (rc.Right, deltaX, maxW);
         var bot = roundUp (rc.Bottom, deltaY, maxH);
         var w = right - left;
         var h = bot - top;
         int diff = (w - h) / 2;
         if (diff>0) {
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
            w = right-left;
         }
         return new RectangleF (left, top, w, h);
      }

      private static void scaleRect (ref Rectangle rc, float scale) {
         rc.X = (int)Math.Floor (rc.X * scale);
         rc.Y = (int)Math.Floor (rc.Y * scale);
         rc.Width = (int)Math.Ceiling (rc.Width * scale);
         rc.Height = (int)Math.Ceiling (rc.Height * scale);
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
               imgFace = extract (img, largerRect);

               //Downscale if needed
               int w = imgFace.Width;
               int h = imgFace.Height;
               float scaleFactor  = Math.Min(MAX / (float)w, MAX / (float)h);
               if (scaleFactor < 1f) {
                  imgFace.Mutate (x => x.Resize ((int)(w* scaleFactor), (int)(h * scaleFactor), KnownResamplers.Lanczos3));
               }

               RotateMode faceOrientation = RotateMode.None;
               bool faceOK = false; 
               if (doRotateCheck) {
                  faceOK = hlp.RotateAndDetectFaces (ref imgFace, rotateBestFactor, out faceOrientation)?.Length>0;
               }
               
               int storId = ++lastFaceStorageId;
               string storKey = storId.ToString ();

               mem.SetLength (0);
               imgFace.SaveAsJpeg (mem, jpgEncoder);
               storages.CurrentFaceStorage.AddBytes (mem.GetBuffer (), 0, (int)mem.Length, storKey, DateTime.UtcNow, CompressMethod.Store);
               if (!faceOK && !doRotateCheck) {
                  imgFace.Dispose ();
                  mem.Position = 0;
                  imgFace = SixLabors.ImageSharp.Image.Load<Rgb24> (mem);
                  faceOK = hlp.HasFace (imgFace);
               }

               //Create and save the embeddings
               var cloned = img.Clone ();
               hlp.Align (cloned, detResult);
               var embeddings = hlp.CreateEmbedding (cloned);
               var bytes = BufferHelper.ToByteArray (embeddings);
               storages.CurrentEmbeddingStorage.AddBytes (bytes, 0, bytes.Length, storKey, DateTime.UtcNow, CompressMethod.Deflate);

               dbFace = new DbFace ();
               dbFace.FaceCount = detResults.Length;
               dbFace.FaceStorageId = storId;
               dbFace.W0 = imgFace.Width;
               dbFace.H0 = imgFace.Height;


               //pw if (rotMode != RotateMode.None) largerRect.Rotate (imgW, imgH, rotMode.ToBackwardRotate ());
               largerRect.Scale (1f / (imgW > imgH ? imgW : imgH)); //Make it relative
               dbFace.RelPos = largerRect.ToSortableString ();
               dbFace.FaceRatio = detResult.GetFaceRatio();
               dbFace.FaceAngleRaw = detResult.GetFaceAngle ();
               dbFace.FaceAngle = dbFace.FaceAngleRaw < 0 ? -1 : ((dbFace.FaceAngleRaw + 45) / 90) * 90 ;
               dbFace.FaceOrientation = faceOrientation;
               dbFace.FaceOK = faceOK;
               dbFaces.Add (dbFace);
               if (cloned != img) cloned.Dispose ();
               cloned = null;
            }


            //and assign IDs
            for (int i=0; i<dbFaces.Count; i++) dbFaces[i].Id = idPrefix + i;
            return dbFaces;
         } finally {
            img?.Dispose ();
         }
      }

      private static int roundDown (float f, int delta) {
         var x = ((int)(f)) - delta;
         return x < 0 ? 0 : x;
      }
      private static int roundUp (float f, int delta, int max) {
         var x = ((int)(.5f + f)) + delta;
         return x > max ? max : x;
      }


   }
}

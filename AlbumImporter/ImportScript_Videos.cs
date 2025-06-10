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

using Bitmanager.Imaging;
using Bitmanager.ImportPipeline.StreamProviders;
using Bitmanager.ImportPipeline;
using Bitmanager.IO;
using Bitmanager.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Drawing;
using Bitmanager.Core;
using System.Drawing.Imaging;
using Bitmanager.Xml;
using Bitmanager.Elastic;
using System.Text.RegularExpressions;
using Bitmanager.IR;
using System.Runtime;
using Bitmanager.Webservices;
using System.Diagnostics;
using MetadataExtractor.Formats.Exif.Makernotes;
using Bitmanager.ImageTools;
using Bitmanager.Storage;
using System.Xml.Schema;
using SixLabors.ImageSharp.Advanced;
using System.Xml;
using Bitmanager.ImportPipeline.Datasources;
using System.Threading;

namespace AlbumImporter {
   public class ImportScript_Videos : ImportScriptBase, IDisposable {
      private readonly MetadataProcessor mdProcessor;
      private readonly HashSet<string> supportedExtAndCodec;
      private IDDatasource idDatasource;
      private string tempConvertFile;
      private string convertExt;
      private string convertCmd, copyCmd, extractCmd;
      private int convertTimeout, copyTimeout, extractTimeout;
      private bool isNewGeneration;
      private bool autoConvert;

      public ImportScript_Videos () {
         supportedExtAndCodec = new HashSet<string> ();
         mdProcessor = new MetadataProcessor ();
      }

      public override void Dispose () {
         base.Dispose ();
         mdProcessor?.Dispose();
      }


      private static string normalizeExt (string ext) {
         return ext[0] == '.' ? ext : "." + ext;
      }
      private static string createKey (string ext, string codec) {
         return (ext + "|" + codec).ToLowerInvariant();
      }
      public object OnDatasourceStart (PipelineContext ctx, object value) {
         Init (ctx, false, 50, false);
         idDatasource = (IDDatasource)ctx.DatasourceAdmin.Datasource;
         tempConvertFile = Path.Combine (ctx.ImportEngine.TempDir, "tmp_conv_video.mp4");

         var convertNode = ctx.DatasourceAdmin.ContextNode.SelectMandatoryNode ("convert");
         autoConvert = convertNode.ReadBool ("@active", true);

         foreach (XmlNode sub in convertNode.SelectMandatoryNodes ("no_convert")) {
            string[] codecs = sub.ReadStr ("@codecs").SplitStandard ();
            string ext = normalizeExt(sub.ReadStr ("@ext"));
            foreach (var c in codecs) {
               supportedExtAndCodec.Add (createKey (ext, c));
            }
         }

         string target;
         if ((ctx.ImportFlags & _ImportFlags.FullImport) != 0) {
            isNewGeneration = true;
            target = videoFramesGeneration.CreateTargetName ();
         } else {
            target = videoFramesGeneration.Target;
            if (target == null) {
               isNewGeneration = true;
               target = videoFramesGeneration.CreateTargetName ();
            }
         }
         videoFrames = new FileStorage (target, isNewGeneration ? FileOpenMode.Create: FileOpenMode.ReadWrite);
         FFMpeg.Logger = Logs.CreateLogger ("ffmpeg", "ffmpeg");

         convertCmd = readFFMpegCmd (ctx.DatasourceAdmin.ContextNode, "commands/convert", out convertTimeout);
         copyCmd = readFFMpegCmd (ctx.DatasourceAdmin.ContextNode, "commands/copy", out copyTimeout);
         extractCmd = readFFMpegCmd (ctx.DatasourceAdmin.ContextNode, "commands/extract_frame", out extractTimeout);
         return value;
      }

      private static string readFFMpegCmd (XmlNode node, string key, out int timeout) {
         var sub = node.SelectMandatoryNode (key);
         string cmd = sub.ReadStr ("@cmd").Replace ('\'', '"');
         if (!cmd.StartsWith ("ffmpeg ", StringComparison.InvariantCultureIgnoreCase)) {
            throw new BMNodeException (node, "Command should start with 'ffmpeg '");
         }
         timeout = sub.ReadInterval ("@timeout", TimeUnit.Minutes, "5");
         return cmd.Substring (7).Trim(); 
      }

      public object OnDatasourceEnd (PipelineContext ctx, object value) {
         ctx.ImportLog.Log ("Errorstate={0}", ctx.ErrorState);
         if (ctx.ErrorState== _ErrorState.OK && isNewGeneration) videoFramesGeneration.CreateOrUpdateLink (deleteSorage);
         IOUtils.DeleteFile (tempFrameFile, DeleteFlags.NoExcept | DeleteFlags.AllowNonExist);
         IOUtils.DeleteFile (tempConvertFile, DeleteFlags.NoExcept | DeleteFlags.AllowNonExist);
         return value;
      }

      private static void deleteSorage (string fn) {
         IOUtils.DeleteFile (Path.ChangeExtension(fn, ".idx"), DeleteFlags.NoExcept | DeleteFlags.AllowNonExist);
         IOUtils.DeleteFile (fn, DeleteFlags.NoExcept | DeleteFlags.AllowNonExist);
      }


      public object OnId (PipelineContext ctx, object value) {
         var idInfo = (IdInfo)value;

         if (MimeType.GetMimeTypeFromFileName(idInfo.FileName).StartsWith("image")) goto EXIT_SKIP;
         if (!File.Exists (idInfo.FileName)) {
            ctx.ImportLog.Log (_LogType.ltWarning, "Missing file: " + idInfo.FileName);
            goto EXIT_SKIP;
         }

         Metadata md = mdProcessor.GetMetadata (idInfo.FileName);
         if (!md.MimeType.StartsWith ("video")) goto EXIT_SKIP;

         var fa = File.GetAttributes (idInfo.FileName);
         ctx.ImportLog.Log ("Video attr={0} file={1}", fa, idInfo.FileName);

         if (!isNewGeneration && videoFrames.GetFileEntry (idInfo.Id) != null && (fa & FileAttributes.Archive) == 0) goto EXIT_SKIP;

         if (autoConvert) {
            if (!supportedExtAndCodec.Contains (createKey (Path.GetExtension(idInfo.FileName), md.CompressorId))) {
               ctx.ImportLog.Log ("converting");
               idInfo = convertVideo (ctx, idInfo, md);
               fa = File.GetAttributes (idInfo.FileName);
            }
         }

         ctx.ImportLog.Log ("extract frame");
         executeFFMpeg ("extracting frame from", extractCmd, idInfo.FileName, tempFrameFile, extractTimeout);
         File.SetAttributes (idInfo.FileName, fa & ~FileAttributes.Archive);

         var bytes = File.ReadAllBytes (tempFrameFile);
         videoFrames.AddBytes (bytes, idInfo.Id, DateTime.UtcNow, CompressMethod.Store);
         ctx.IncrementAdded ();
         return value;

      EXIT_SKIP:
         ctx.ActionFlags |= _ActionFlags.Skip;
         return value;


      }

      public object OnRestore (PipelineContext ctx, object value) {
         var idInfo = (IdInfo)value;

         if (MimeType.GetMimeTypeFromFileName (idInfo.FileName).StartsWith ("image")) goto EXIT_SKIP;
         if (!File.Exists (idInfo.FileName)) {
            if (File.Exists (idInfo.FileName + ".mp4")) {
               if (!restoreFile (ctx, idInfo.FileName + ".mp4")) goto EXIT_SKIP;
               ctx.IncrementAdded ();
               goto EXIT_RTN;
            }
            goto EXIT_SKIP;
         }

         if (!idInfo.FileName.EndsWith (".mp4")) goto EXIT_SKIP;

         Metadata md = mdProcessor.GetMetadata (idInfo.FileName);
         if (!md.MimeType.StartsWith ("video")) goto EXIT_SKIP;

         if (!restoreFile (ctx, idInfo.FileName)) goto EXIT_SKIP;
         ctx.IncrementAdded ();
         goto EXIT_RTN;

      EXIT_SKIP:
         ctx.ActionFlags |= _ActionFlags.Skip;

      EXIT_RTN:
         return value;
      }


      private bool restoreFile (PipelineContext ctx, string mp4File) {
         string dir = Path.GetDirectoryName (mp4File);
         string mask = Path.GetFileNameWithoutExtension (mp4File) + ".*_";
         var files = Directory.GetFiles (dir, mask);
         if (files.Length != 1) {
            ctx.ImportLog.Log (_LogType.ltWarning, "Cannot restore {0}: found {1} files with an ending '_'.", mp4File, files.Length);
            return false;
         }

         var savedFn = files[0];
         string orgFn = savedFn.Substring(0, savedFn.Length - 1);

         ctx.ImportLog.Log (_LogType.ltInfo, "Delete {0}", mp4File);
         IOUtils.DeleteFile (mp4File, DeleteFlags.AllowNonExist);
         ctx.ImportLog.Log (_LogType.ltInfo, "Rename {0} into {1}", savedFn, orgFn);
         File.Move (savedFn, orgFn);
         return true;
      }

      private void executeFFMpeg (string action, string fmt, string src, string dst, int timeout) {
         var psi = FFMpeg.CreatePsi (Invariant.Format (fmt, src, dst));
         var result = FFMpeg.Execute (psi, timeout);
         if (result.HasError () || !File.Exists (dst)) {
            var sb = new StringBuilder ();
            sb.AppendFormat ("Error while {0} {1}\n", action, src);
            result.AppendMsg (sb);
            throw new BMException (sb.ToString ());
         }
      }
      
      private IdInfo convertVideo(PipelineContext ctx, IdInfo idInfo, Metadata md) {
         ctx.ImportLog.Log ("Converting " + idInfo.FileName);

         executeFFMpeg ("converting video", 
                        convertCmd, 
                        idInfo.FileName, 
                        tempConvertFile,
                        convertTimeout);

         var dtMod = File.GetLastWriteTimeUtc (idInfo.FileName);
         var dtCre = File.GetCreationTimeUtc (idInfo.FileName);
         File.Move (idInfo.FileName, idInfo.FileName + "_");

         string newFn = Path.ChangeExtension (idInfo.FileName, ".mp4");
         File.Copy (tempConvertFile, newFn);
         File.SetLastWriteTimeUtc (newFn, dtMod);
         File.SetCreationTimeUtc (newFn, dtCre);
         idDatasource.UpdateExt (".mp4");
         return new IdInfo (Path.ChangeExtension (idInfo.Id, ".mp4"), idInfo.User, newFn);
      }

      private int getRotate (string fn) {
         fn = Path.ChangeExtension (fn, ".thm");
         if (!File.Exists (fn)) return 0;
         var md = mdProcessor.GetMetadata (fn);
         return md == null ? 0 : md.Orientation.AsInt ();
      }

   }
}

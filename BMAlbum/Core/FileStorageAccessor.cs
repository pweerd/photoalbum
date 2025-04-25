/*
 * Copyright © 2024, De Bitmanager
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

using Bitmanager.Core;
using Bitmanager.IO;
using Bitmanager.Storage;
using System;
using System.Drawing;

namespace BMAlbum.Core {
   /// <summary>
   /// Helper class to parallel access the FileStorage.
   /// This should be moved to core as soon as that is ported to .NetCore
   /// </summary>
   public static class FileStorageAccessor {
      public static byte[] GetBytes (FileStorage stor, FileEntry e) {
         if (e==null || e.Stale) return null;
         switch (e.CompressMethod & CompressMethod.CompressMask) {
            case CompressMethod.Store:
               break;
            case CompressMethod.Deflate:
               return getDecompressedBytes (stor, e);
            default:
               e.CompressMethod.ThrowUnexpected ();
               break;
         }
         return read (stor, e, e.Size);
      }
      private static unsafe byte[] getDecompressedBytes (FileStorage stor, FileEntry e) {
         var compressedBytes = read (stor, e, e.CompressedSize);
         var ret = new byte [e.Size];
         var compressor = CoreHelper.Instance.CreateCompressor ();
         try {
            compressor.ZlibDecompressInit ((int)ZLibWindowBits.Deflate);
            int outPos = 0;
            int outEnd = ret.Length;
            int curPos = 0;
            int curEnd = compressedBytes.Length;
            while (true) {
               if (outPos >= outEnd || curPos >= curEnd) break;
               int srclen = curEnd - curPos;
               int dstlen = outEnd - outPos;

               fixed (byte* pDst = ret)
               fixed (byte* pSrc = compressedBytes) {
                  ZLibReturnCode rc = (ZLibReturnCode)compressor.ZlibDecompress (
                     ref *(pSrc + curPos),//scurBuffer[curPos], 
                     ref *(pDst + outPos), //buffer[outPos], 
                     ref srclen,
                     ref dstlen);

                  outPos += dstlen;
                  curPos += srclen;

                  switch (rc) {
                     case ZLibReturnCode.OK: continue;

                     case ZLibReturnCode.BUFFER_ERROR:
                     case ZLibReturnCode.STREAM_END: goto EXIT_LOOP;
                  }
               }
            }
         EXIT_LOOP:
            compressor.ZlibDecompressCleanup ();
            if (outPos < outEnd)
               throw new BMException ("Decompress did not deliver all bytes: {0} from expected {1}.\nFile={2}\nEntry={3}.", outPos, outEnd, stor.FileName, e.Name);
         } finally {
            Utils.FreeAndNil(ref compressor);
         }
         return ret;
      }

      private static byte[] read (FileStorage stor, FileEntry e, long len) {
         if (len > int.MaxValue) throw new BMException ("Cannot read more than 2GB: len={0}", len);
         long offset = e.Offset;
         int todo = (int)len;
         int bufOffset = 0;
         var ret = new byte[todo];
         while (todo > 0) {
            int read = RandomAccess.Read (stor.SafeFileHandle, new Span<byte>(ret, bufOffset, todo), offset);
            if (read == 0) break;
            todo -= read;
            bufOffset += read;
            offset += read;
         }
         if (bufOffset < len)
            throw new BMException ("Cannot read {0} bytes from offset {1}.\nFile={2}\nEntry={3}.", len, e.Offset, stor.FileName, e.Name);
         return ret;
      }
   }
}

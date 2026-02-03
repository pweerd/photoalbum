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

using CoenM.ImageHash.HashAlgorithms;
using Shipwreck.Phash;
using Shipwreck.Phash.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.Intrinsics.X86;
using System.Text;
using System.Threading.Tasks;

namespace AlbumImporter.PHash {
   public class PHash {
      public static ulong GetFingerprint(string fn) {
         using (var img = Image.Load<Rgb24>(fn)) {
            int w= img.Width;
            int h = img.Height;
            var lum = new ByteImage(w, h);
            var arr = lum.Array;
            int index = 0;
            img.ProcessPixelRows((accessor) => {
               for (int i = 0; i < h; i++) {
                  Span<Rgb24> row = accessor.GetRowSpan(i);
                  for (int j = 0; j < w; j++) {
                     int v = row[j].R * 66 + row[j].R * 129 + row[j].B * 25;
                     arr[index++] = (byte)(((v + 128) >> 8) + 16);
                  }
               }
            });
            return ImagePhash.ComputeDctHash(lum);
         }
      }
      public static ulong GetFingerprint2(string fn) {
         using (var img = Image.Load<Rgb24>(fn)) {
            img.Mutate(x => x.Resize(32, 32).Grayscale(GrayscaleMode.Bt601));
            int w= img.Width;
            int h = img.Height;
            var lum = new ByteImage(w, h);
            var arr = lum.Array;
            int index = 0;
            img.ProcessPixelRows((accessor) => {
               for (int i = 0; i < h; i++) {
                  Span<Rgb24> row = accessor.GetRowSpan(i);
                  for (int j = 0; j < w; j++) {
                     arr[index++] = row[j].R;
                  }
               }
            });
            return ImagePhash.ComputeDctHash(lum);
         }
      }
      public static ulong GetFingerprint2r(string fn) {
         using (var img = Image.Load<Rgb24>(fn)) {
            img.Mutate(x => x.Resize(32, 32).Grayscale().AutoOrient());
            int w= img.Width;
            int h = img.Height;
            var lum = new ByteImage(w, h);
            var arr = lum.Array;
            int index = 0;
            img.ProcessPixelRows((accessor) => {
               for (int i = 0; i < h; i++) {
                  Span<Rgb24> row = accessor.GetRowSpan(i);
                  for (int j = 0; j < w; j++) {
                     arr[index++] = row[j].R;
                  }
               }
            });
            return ImagePhash.ComputeDctHash(lum);
         }
      }

      static readonly PerceptualHash hasher = new PerceptualHash();
      public static ulong GetFingerprint3(string fn) {
         using (var img = Image.Load<Rgba32>(fn)) {
            return hasher.Hash(img);
         }
      }

      public static int GetHammingDistance(ulong v) {
         unchecked {
            if (Popcnt.X64.IsSupported) {
               return (int)Popcnt.X64.PopCount(v);
            }
            v = v - ((v >> 1) & 0x5555555555555555UL);
            v = (v & 0x3333333333333333UL) + ((v >> 2) & 0x3333333333333333UL);
            return (int)((((v + (v >> 4)) & 0xF0F0F0F0F0F0F0FUL) * 0x101010101010101UL) >> 56);
         }
      }


      static readonly char[] nibbles = {'0', '1', '2', '3', '4', '5', '6', '7', '8', '9', 'A', 'B', 'C', 'D', 'E', 'F'};
      public static string FingerprintToString(ulong fp) {
         var sb = new StringBuilder (4*64);
         for (int i =0; i<64; i++) {
            sb.Append(' ');
            sb.Append ((char)(((int)(fp>>i) & 1) + '0'));
            sb.Append(nibbles[i / 16]);
            sb.Append(nibbles[i % 16]);
         }
         return sb.ToString(1, sb.Length-1);
      }
   }
}

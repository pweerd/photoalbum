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

using Bitmanager.Imaging;
using System.Drawing;

namespace BMAlbum.Core {
   public class ShrinkExact : ShrinkOperation {
      public override bool Process (ShrinkContext ctx, int w, int h) {
         var factor = ImageShrinker.GetFactor (ctx.Bitmap.Width, ctx.Bitmap.Height, w, h);
         if (factor > 1.2) {
            ImageShrinker.ResizeAndReplaceGDI (ref ctx.Bitmap,
               (int)(.5 + ctx.Bitmap.Width / factor),
               (int)(.5 + ctx.Bitmap.Height / factor),
               false
            );
         }
         return true;
      }

      /// <summary>
      /// Always use 99 as a fingerprint
      /// </summary>
      public override bool GetFingerprint (int srcW, int srcH, int w, int h, ref int fp) {
         fp = fp*100+99;
         return true;
      }

   }

}

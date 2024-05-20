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
using Bitmanager.Xml;
using System.Drawing;
using System.Xml;

namespace BMAlbum.Core {
   public class ShrinkFactor2 : ShrinkOperation {
      public readonly double Bias20;

      public ShrinkFactor2 (double bias) {
         Bias20 = bias;
      }
      public ShrinkFactor2 (XmlNode node) {
         Bias20 = node.ReadFloat ("@bias");
      }
      public override bool Process (ShrinkContext ctx, int w, int h) {
         var factor = ImageShrinker.GetFactor (ctx.Bitmap.Width, ctx.Bitmap.Height, w, h);
         var factorLog = ImageShrinker.GetFactorLog (factor, ImageShrinker.BaseLog20, Bias20);
         if (factorLog >= 1) {
            ImageShrinker.ResizeAndReplacePower2 (ref ctx.Bitmap, (int)(.5 + Math.Pow (2.0, factorLog)));
         }
         return true;
      }
      public override bool GetFingerprint (Bitmap bm, int w, int h, ref int fp) {
         var factor = ImageShrinker.GetFactor (bm.Width, bm.Height, w, h);
         var factorLog = ImageShrinker.GetFactorLog (factor, ImageShrinker.BaseLog20, Bias20);
         if (factorLog >= 1) fp = 10 * fp + factorLog;
         return true;
      }
   }

}

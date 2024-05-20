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
   public class CheckMinFactor : ShrinkOperation {
      public readonly double MinFactor;
      public CheckMinFactor (double minFactor) {
         MinFactor = minFactor;
      }
      public CheckMinFactor (XmlNode node) {
         MinFactor = node.ReadFloat ("@factor");
      }

      public override bool Process (ShrinkContext ctx, int w, int h) {
         return ImageShrinker.GetFactor (ctx.Bitmap.Width, ctx.Bitmap.Height, w, h) >= MinFactor;
      }
      public override bool GetFingerprint (Bitmap bm, int w, int h, ref int fp) {
         if (ImageShrinker.GetFactor (bm.Width, bm.Height, w, h) >= MinFactor) return true;
         fp = 0;
         return false;
      }
   }

}

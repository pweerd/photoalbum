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
using System.Xml;

namespace BMAlbum.Core {
   public class ShrinkGausseSharpen : ShrinkOperation {
      public readonly int CenterWeight;
      public readonly bool OnlyIfChanged;

      public ShrinkGausseSharpen (int centerWeight, bool onlyIfChanged) {
         CenterWeight = centerWeight;
         OnlyIfChanged = onlyIfChanged;
      }
      public ShrinkGausseSharpen (XmlNode node) {
         CenterWeight = node.ReadInt ("@center");
         OnlyIfChanged = !node.ReadBool ("@always", false);
      }

      public override bool Process (ShrinkContext ctx, int w, int h) {
         if (!OnlyIfChanged || ctx.Changed) {
            ImageShrinker.GausseSharpenAndReplace (ref ctx.Bitmap, CenterWeight);
         }
         return true;
      }
   }

}

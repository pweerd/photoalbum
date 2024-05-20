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
using System.Drawing.Imaging;

namespace BMAlbum.Core {
   public class ShrinkContext {
      public readonly Bitmap BitmapOrg;
      public Bitmap Bitmap;
      public PropertyItem[] Props;

      public ShrinkContext(Bitmap bm) {
         BitmapOrg = bm;
         Bitmap = bm;
         Props = bm.PropertyItems;
      }

      public bool Changed => !object.ReferenceEquals (BitmapOrg, Bitmap); 

      public bool CopyPropertiesIfChanged() {
         if (!Changed) return false;
         ImageShrinker.CopyProperties(Bitmap, Props, true);
         return true;
      }
   }

}

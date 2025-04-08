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

using System.Drawing;

namespace BMAlbum.Core {
   public abstract class ShrinkOperation {
      public abstract bool Process (ShrinkContext ctx, int targetW, int targetH);

      public virtual bool GetFingerprint (int srcW, int srcH, int targetW, int targetH, ref int fp) {
         return true;
      }
   }

}

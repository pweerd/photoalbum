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

namespace BMAlbum {
   public class CountedNumber {
      public readonly int Number;
      public int Count;
      public CountedNumber (int h) {
         Number = h;
         Count = 1;
      }
      public CountedNumber (int h, int c) {
         Number = h;
         Count = c;
      }

      public static int SortCount (CountedNumber x, CountedNumber y) {
         return y.Count - x.Count;
      }

      public override string ToString () {
         return Invariant.Format ("[h={0}, count={1}]", Number, Count);
      }
   }



}
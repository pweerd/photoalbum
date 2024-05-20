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
using Bitmanager.Json;

namespace BMAlbum {
   public class CacheStats : IJsonSerializable {
      public readonly List<CountedNumber> Numbers;
      public readonly long Size;
      public readonly int Count;
      public readonly CacheType Type;

      public CacheStats (CacheType type, List<CountedNumber> numbers, int count, long size) {
         Numbers = numbers;
         Count = count;
         Size = size;
         Type = type;
      }

      public int[] GetMostUsedNumbers (int count = 5) {

         int N = Numbers.Count;
         if (count > 0 && count < N) N = count;
         int[] ret = new int[N];
         for (int i = 0; i < ret.Length; i++) {
            ret[i] = Numbers[i].Number;
         }
         return ret;
      }

      public void WriteTo (JsonWriter wtr) {
         wtr.WriteStartObject ();

         wtr.WriteProperty ("count", Count);
         wtr.WriteProperty ("size", Size);
         wtr.WriteProperty ("size_str", Pretty.PrintSize(Size));
         wtr.WritePropertyName ("dims");
         wtr.WriteStartArray ();
         foreach (var n in Numbers) {
            wtr.WriteStartObject ();
            wtr.WriteProperty ("number", n.Number);
            wtr.WriteProperty ("count", n.Count);
            wtr.WriteEndObject ();
         }
         wtr.WriteEndArray ();

         wtr.WriteEndObject ();
      }
   }



}
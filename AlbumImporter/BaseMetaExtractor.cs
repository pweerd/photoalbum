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

using Bitmanager.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AlbumImporter {
   /// <summary>
   /// Base class for the Directory- and FileMetaExtractor
   /// </summary>
   public class BaseMetaExtractor {
      public static bool onlyDigits (string s) {
         if (string.IsNullOrEmpty (s)) return false;
         for (int i = 0; i < s.Length; i++) {
            if (s[i] < '0' || s[i] > '9') return false;
         }
         return true;
      }

      protected static readonly char[] seps = new char[] { ' ', '_', '-' };
      static readonly string[] EMPTY = new string[0];
      public static string[] split (string s) {
         if (string.IsNullOrEmpty (s)) return EMPTY;
         return s.Split (seps, int.MaxValue, StringSplitOptions.RemoveEmptyEntries);
      }

      public static string ToStrDate(DateTime dt) {
         return dt == DateTime.MinValue ? null : dt.ToLocalTimeAssumeLocalIfUns ().ToString ("yyyy-MM-dd");
      }

      protected static bool isValidTime (int h, int m, int s) {
         return s >= 0 && s < 60 && m >= 0 && m < 60 && h >= 0 && h < 24;
      }
      protected static bool isValidDate (int y, int m, int d) {
         return isValidYear(y) && isValidMonth(m) && isValidDay(d);
      }
      protected static bool isValidYear (int y) {
         return y > 1900 && y < 2200;
      }
      protected static bool isValidMonth (int m) {
         return m > 0 && m <= 12;
      }
      protected static bool isValidDay (int d) {
         return d > 0 && d <= 31;
      }
      protected static bool tryParseYearOrMMDD (long num, ref int yy, ref int mm, ref int dd) {
         bool ret;
         if (ret=isValidYear((int)num)) {
            yy = (int)num;
            mm = 1;
            dd = 1;
         } else {
            if (isValidYear(yy)) {
               int m = (int)(num / 100);
               int d = (int)(num % 100);
               if (isValidMonth(m) && isValidDay(d)) {
                  ret = true;
                  mm = m;
                  dd = d;
               }
            }
         }
         return ret;
      }
      protected static bool tryParseDate (long num, ref int yy, ref int mm, ref int dd) {
         bool ret = false;
         int y = (int)(num / 10000);
         int m = (int)((num / 100) % 100);
         int d = (int)(num % 100);
         if (isValidDate (y, m, d)) {
            yy = y;
            mm = m;
            dd = d;
            ret = true;
         }
         return ret;
      }

      protected static bool tryParseTime (long num, ref int hh, ref int mm, ref int ss) {
         bool ret = false;
         int s = (int)(num % 100);
         int m = (int)((num / 100) % 100);
         int h = (int)((num / 10000) % 100);
         if (isValidTime(h, m, s)) {
            hh = h;
            mm = m;
            ss = s;
            ret = true;
         }
         return ret;
      }
   }
}

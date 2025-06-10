using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AlbumImporter {
   internal static class Trim {
      public static bool IsSep (char c) {
         switch (c) {
            case ' ':
            case ',':
            case ';':
            case '\\':
            case '/':
            case '-':
            case '_': return true;
         }
         return false;
      }


      public static int TrimRight (string s, int start, int end) {
         for (int i = end; i > start; i--) {
            if (!Trim.IsSep (s[i - 1])) return i;
         }
         return start;
      }
      public static int TrimLeft (string s, int start, int end) {
         for (int i = start; i < end; i++) {
            if (!Trim.IsSep (s[i])) return i;
         }
         return end;
      }

      public static int TrimRightParenthesis (string s, int start, int end) {
         int limit = end - 4;
         if (limit < start) limit = start;

         for (int i = end; i > limit; i--) {
            if (s[i - 1] == '(') return TrimRight (s, start, i - 1);
         }
         return end;
      }

      public static int TrimLeftNumbers (string s, int start, int end) {
         for (int i = start; i < end; i++) {
            if (s[i] >= '0' && s[i] <= '9') continue;
            if (char.IsLetter (s[i])) return start;
            return TrimLeft (s, i, end);
         }
         return end;
      }
      public static int TrimRightNumbers (string s, int start, int end) {
         for (int i = end; i > start; i--) {
            if (s[i - 1] >= '0' && s[i - 1] <= '9') continue;
            if (char.IsLetter (s[i - 1])) return end;
            return TrimRight (s, start, i);
         }
         return start;
      }

   }
}

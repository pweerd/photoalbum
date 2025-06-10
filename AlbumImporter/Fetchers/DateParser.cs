namespace AlbumImporter {
   [Flags]
   public enum DateParserFlags { 
      None, 
      AllowYearOnly=1,
      AllowTimeOnly = 2,
      AllowYearMonth = 4,
      DisallowTime = 8,
      UseEndingNumber = 16,

   }
   public class DateParser2 {
      const int TIME = 1, DATE = 2;
      public int yy, mm, dd;
      public int hrs, min, sec;
      private readonly DateParserFlags flags;
      private int state;
      private readonly int initialState;
      public DateParser2 (DateParserFlags flags) {
         hrs = -1;
         min = -1;
         sec = -1;
         state = initialState = 0;
         this.flags = flags;
      }
      public DateParser2 (DateParser2 other, DateParserFlags flags) {
         this.flags = flags;
         initialState = other.state;
         yy = other.yy;
         mm = other.mm;
         dd = other.dd;
         hrs = other.hrs;
         min = other.min;
         sec = other.sec;
      }

      public bool Valid => state > initialState;
      public bool HasTime => state > 3;

      public DateTime ToDate (DateTimeKind kind = DateTimeKind.Local) {
         DateTime dt;
         if (state > 3) {
            if ((flags & DateParserFlags.DisallowTime) != 0) goto RET_DATE;
            goto RET_DATETIME;
         }
         if (state > 0) goto RET_DATE;

         return DateTime.MinValue;

      RET_DATETIME: return new DateTime (yy, mm, dd, hrs, min, sec, kind);
      RET_DATE: return new DateTime (yy, mm, dd, 0, 0, 0, kind);
      }


      public bool Parse (string s) {
         reset ();
         bool ret = false;

         //Trim ending underscores (they are used as a hidden-mark)
         int N = s.Length;
         while (N>0) {
            if (s[N - 1] != '_') break;
            --N;
         }
         int num = 0;
         int count = 0;
         for (int i = 0; i < N; i++) {
            var ch = (int)s[i];
            if (ch >= '0' && ch <= '9') {
               num = num * 10 + (ch - '0');
               count++;
               continue;
            }

            //Handle date/time separators
            if (count > 0) {
               ret |= processNumber (num, count);
               count = 0;
               num = 0;
            }

         }
         if (count > 0 && (flags & DateParserFlags.UseEndingNumber) != 0)
            ret |= processNumber (num, count);

         if (ret) {
            if (state == 1 && (flags & DateParserFlags.AllowYearOnly) == 0) return false;
            return true;
            //if (yy > 0 || (flags & DateParserFlags.AllowTimeOnly) != 0) return true;
         }
         return false;
      }

      private static bool validYear (int num) {
         return num > 1900 && num < 2100;
      }
      private static bool validMonth (int num) {
         return num > 0 && num <= 12;
      }
      private static bool validDay (int num) {
         return num > 0 && num <= 31;
      }
      private static bool validHour (int num) {
         return num >= 0 && num < 24;
      }
      private static bool validMin (int num) {
         return num >= 0 && num < 60;
      }
      private static bool validSec (int num) {
         return num >= 0 && num < 60;
      }

      private bool processNumber (int num, int count) {
         int y, m, d;
         switch (count) {
            case 2:
               if (state == 0) return false;
               return processNumber (num);
            case 4: //might be year or mmdd
               if (state==0) return processNumber (num);

               m = num / 100;
               if (!validMonth (m)) goto INVALID;
               d = num % 100;
               if (!validDay (d)) goto INVALID;
               mm = m;
               dd = d;
               state = 3;
               return true;
            case 6: //might be time or yyyymm
               var possibleTime = validSec (num % 100) && validMin ((num / 100) % 100) && validHour (num / 10000);
               var possibleDate = validMonth (num % 100) && validYear (num / 100);
               if (state == 0) {
                  if (possibleDate) {
                     if ((flags & DateParserFlags.AllowYearMonth) == 0) goto INVALID;
                     yy = num / 100;
                     mm = num % 100;
                     state = 2;
                     return true;
                  }
               }
               if (state == 3) {
                  if (possibleTime) {
                     hrs = num / 10000;
                     min = (num / 100) % 100;
                     sec = num % 100;
                     state = 6;
                     return true;
                  }
               }
               goto INVALID; ;

            case 8:
               d = num % 100;
               m = (num / 100) % 100;
               y = num / 10000;
               if (validDay(d) && validMonth(m) && validYear(y)) {
                  yy = y;
                  mm = m;
                  dd = d;
                  state = 3;
                  return true;
               }
               goto INVALID;
         }
         INVALID:
         return false;
      }
      private bool processNumber (int num) {
         switch (state) {
            default: goto INVALID;
            case 0: //year
               if (!validYear(num)) goto INVALID;
               yy = num;
               mm = 1;
               dd = 1;
               goto OK;
            case 1: //month
               if (!validMonth (num)) goto INVALID;
               mm = num;
               goto OK;
            case 2: //day
               if (!validDay (num)) goto INVALID;
               dd = num;
               goto OK;
            case 3: //hour
               if (!validHour (num)) goto INVALID;
               hrs = num;
               min = 0;
               sec = 0;
               goto OK;
            case 4: //minute
               if (!validMin (num)) goto INVALID;
               min = num;
               goto OK;
            case 5: //second
               if (!validSec (num)) goto INVALID;
               sec = num;
               goto OK;
         }

      INVALID: return false;
      OK:
         state++;
         return true;
      }


      private void reset () {
         state = initialState;
      }

   }
   //public class DateParser {
   //   const int TIME = 1, DATE = 2;
   //   public int yy, mm, dd;
   //   public int hrs, min, sec;
   //   private readonly DateParserFlags flags;
   //   public DateParser (DateParserFlags flags) {
   //      hrs = -1;
   //      min = -1;
   //      sec = -1;
   //      this.flags = flags;
   //   }

   //   public DateTime ToDate (DateTimeKind kind = DateTimeKind.Local) {

   //      return hrs < 0 ? new DateTime (yy, mm, dd, 0, 0, 0, kind)
   //                     : new DateTime (yy, mm, dd, hrs, min, sec, kind);
   //   }


   //   public bool Parse (string s) {
   //      bool ret = false;
   //      int num = 0;
   //      int count = 0;
   //      int timeOrDate = 0;
   //      int tmpTimeOrDate = (flags & DateParserFlags.DisallowTime) != 0 ? DATE : 0;
   //      reset ();
   //      for (int i = 0; i < s.Length; i++) {
   //         var ch = (int)s[i];
   //         if (ch >= '0' && ch <= '9') {
   //            num = num * 10 + (ch - '0');
   //            count++;
   //            timeOrDate = tmpTimeOrDate;
   //            continue;
   //         }

   //         //Handle date/time separators
   //         if (count > 0) {
   //            switch (ch) {
   //               case ':': tmpTimeOrDate |= TIME; continue;
   //               case ' ':
   //                  if ((flags & DateParserFlags.DisallowTime) == 0) break; //Time allowed: treat ' ' as sep between date and time
   //                  continue;
   //               case '/':
   //               case '\\':
   //               case '-':
   //               case '_':
   //                  if (count == 8) break; //Maybe this is a separator between date and time
   //                  tmpTimeOrDate |= DATE; continue;
   //            }
   //         }

   //         if (count != 0 && parseDateOrTime (num, count, timeOrDate)) ret = true;
   //         count = 0;
   //         num = 0;
   //         timeOrDate = 0;
   //         tmpTimeOrDate = 0;
   //      }
   //      if (count != 0 && parseDateOrTime (num, count, timeOrDate)) ret = true;

   //      if (ret) {
   //         if (yy > 0 || (flags & DateParserFlags.AllowTimeOnly) != 0) return true;
   //      }
   //      reset ();
   //      return false;
   //   }


   //   private void reset () {
   //      yy = 0;
   //      mm = 0;
   //      dd = 0;
   //      hrs = -1;
   //      min = -1;
   //      sec = -1;
   //   }

   //   private static bool validYear (int x) {
   //      return x >= 1900 && x < 2100;
   //   }
   //   private static bool validMonth (int x) {
   //      return x > 0 && x <= 12;
   //   }

   //   private bool parseDateOrTime (int x, int count, int dateOrTime) {

   //      switch (dateOrTime) {
   //         case 0:
   //            switch (count) {
   //               case 4:
   //               case 8:
   //                  if (parseDate (x, count)) return true;
   //                  goto INVALID;
   //               case 6:
   //                  if (parseDate (x, count)) return true;
   //                  return parseTime (x);
   //            }
   //            goto INVALID;
   //         case TIME:
   //            if (count == 6) return parseTime (x);
   //            goto INVALID;
   //         case DATE:
   //            return parseDate (x, count);
   //      }

   //   INVALID:
   //      return false;
   //   }
   //   private bool parseTime (int time) {
   //      if (hrs >= 0) goto INVALID;
   //      if (time <= 0) goto INVALID;
   //      var ss = time % 100;
   //      if (ss >= 60) goto INVALID;
   //      var mm = (time / 100) % 100;
   //      if (mm >= 60) goto INVALID;
   //      var hh = time / 10000;
   //      if (hh >= 24) goto INVALID;

   //      hrs = hh;
   //      min = mm;
   //      sec = ss;
   //      return true;

   //   INVALID:
   //      return false;
   //   }

   //   private bool parseDate (int date, int count) {
   //      if (yy > 0) goto INVALID;
   //      if (date <= 0) goto INVALID;
   //      int d = 1, m = 1, y;
   //      switch (count) {
   //         case 8: break;
   //         case 6: goto DATE6;
   //         case 4: goto DATE4;
   //         default: goto INVALID;
   //      }

   //      d = date % 100;
   //      if (d > 31) goto INVALID;
   //      m = (date / 100) % 100;
   //      if (m < 1 || m > 12) goto INVALID;
   //      y = date / 10000;
   //      if (y < 1900 || y > 2100) goto INVALID;
   //      goto VALID;

   //   DATE6:
   //      if ((flags & DateParserFlags.AllowYearMonth) == 0) goto INVALID;
   //      m = date % 100;
   //      if (m < 1 || m > 12) goto INVALID;
   //      y = date / 100;
   //      if (y < 1900 || y > 2100) goto INVALID;
   //      goto VALID;

   //   DATE4:
   //      if ((flags & DateParserFlags.AllowYearOnly) == 0) goto INVALID;
   //      y = date;
   //      if (y < 1900 || y > 2100) goto INVALID;
   //      goto VALID;

   //   VALID:
   //      yy = y;
   //      mm = m;
   //      dd = d;
   //      return true;

   //   INVALID:
   //      return false;
   //   }
   //}
}

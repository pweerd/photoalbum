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

//using System.Drawing;

using Bitmanager.Core;

namespace AlbumImporter {
   /// <summary>
   /// Determines the origin of face data.
   /// NB Everything bigger or equal to Manual survives a full import
   /// </summary>
   [Flags]
   //public enum NameSource { NotAssigned = 0, Manual = 1, Unknown, Corrected, Auto, AutoUnknown, AutoCorrected };
   public enum NameSource { 
      NotAssigned = 0, 
      Manual = 1,
      Corrected = 2,
      Auto = 4,

      Known = 0x10,
      Unknown = 0x20, 
   };

   public static class NameSourceExtensions {

      public static bool CanHaveName (this NameSource ns) {
         return (ns & NameSource.Known) != 0;
      }
      public static bool IsKnown (this NameSource ns) {
         return (ns & NameSource.Known) != 0;
      }
      public static bool IsManualDefined (this NameSource ns) {
         return (ns & (NameSource.Manual | NameSource.Corrected)) != 0;
      }
      public static bool IsAutoAssigned (this NameSource ns) {
         return (ns & NameSource.Auto) != 0;
      }
      public static NameSource ToAuto (this NameSource ns) {
         return (ns & (NameSource.Known | NameSource.Unknown)) | NameSource.Auto;
      }

      public static string ToString (this NameSource ns) {
         string ret = "N";
         switch (ns) {
            case NameSource.NotAssigned:
               break;
            case NameSource.Manual | NameSource.Known:
               ret = "MK"; break;
            case NameSource.Manual | NameSource.Unknown:
               ret = "MU"; break;
            case NameSource.Corrected | NameSource.Known:
               ret = "CK"; break;
            case NameSource.Corrected | NameSource.Unknown:
               ret = "CU"; break;
            case NameSource.Auto | NameSource.Known:
               ret = "AK"; break;
            case NameSource.Auto | NameSource.Unknown:
               ret = "AU"; break;
            default:
               ns.ThrowUnexpected (); break;
         }
         return ret;
      }

      public static NameSource FromString (string x) {
         var ret = NameSource.NotAssigned;
         if (x == null) goto EXIT_RTN;

         switch (x.Length) {
            case 0: goto EXIT_RTN;
            case 1:
               switch (x[0]) {
                  case 'N': goto EXIT_RTN;
               }
               break;
            case 2:
               switch (x[0]) {
                  default: goto ERROR;
                  case 'M': ret = NameSource.Manual; break;
                  case 'C': ret = NameSource.Corrected; break;
                  case 'A': ret = NameSource.Auto; break;
               }
               switch (x[1]) {
                  default: goto ERROR;
                  case 'U': ret |= NameSource.Unknown; goto EXIT_RTN;
                  case 'K': ret |= NameSource.Known; goto EXIT_RTN;
               }
               break;
         }
      ERROR:
         throw new BMException ("Invalid value for NameSource: [{0}]", x);

      EXIT_RTN:
         return ret;
      }
   }
}

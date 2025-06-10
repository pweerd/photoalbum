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
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace AlbumImporter {
   public static class RectangleExtensions {
      public static float Area (this RectangleF rect) {
         return rect.Width * rect.Height;
      }
      public static float Overlap (this in RectangleF rect, in RectangleF other) {
         float w = Math.Min (rect.Right, other.Right) - Math.Max (rect.X, other.X);
         if (w <= 0f) return 0f;

         float h = Math.Min (rect.Bottom, other.Bottom) - Math.Max (rect.Y, other.Y);
         return (h <= 0f) ? 0f : w * h;
      }

      public static void Scale (this ref RectangleF rect, float factor) {
         rect.X = rect.X * factor;
         rect.Y = rect.Y * factor;
         rect.Width = rect.Width * factor;
         rect.Height = rect.Height * factor;
      }
      public static void OldScale (this ref RectangleF rect, float factorW, float factorH) {
         rect.X = rect.X * factorW;
         rect.Y = rect.Y * factorH;
         rect.Width = rect.Width * factorW;
         rect.Height = rect.Height * factorH;
      }

      public static RectangleF Clone (this in RectangleF rect) {
         return new RectangleF (rect.X, rect.Y, rect.Width, rect.Height);
      }

      public static void Rotate (this ref RectangleF rect, float imgWidth, float imgHeigt, RotateMode rotMode) {
         float tmp;
         switch (rotMode) {
            case RotateMode.None: break;
            case RotateMode.Rotate270: 
               tmp = rect.X;
               rect.X = rect.Y;
               rect.Y = imgWidth - tmp - rect.Width;
               tmp = rect.Width;
               rect.Width = rect.Height;
               rect.Height = tmp;
               break;
            case RotateMode.Rotate180: 
               rect.X = imgWidth - rect.X - rect.Width;
               rect.Y = imgHeigt - rect.Y - rect.Height;
               break;
            case RotateMode.Rotate90: 
               tmp = rect.X;
               rect.X = imgHeigt - rect.Y - rect.Height;
               rect.Y = tmp;
               tmp = rect.Width;
               rect.Width = rect.Height;
               rect.Height = tmp;
               break;
         }
      }

      public static string ToSortableString (this in RectangleF rect) {
         return Invariant.Format ("{0:F5},{1:F5},{2:F5},{3:F5}", rect.X, rect.Y, rect.Width, rect.Height);
      }


      public static RectangleF ToRectangle (this string str, float scale) {
         string[] arr = str.Split (',');
         if (arr.Length != 4) throw new BMException ("Invalid sortable rectangle string [{0}].", str);

         float x = Invariant.ToFloat (arr[0]);
         float y = Invariant.ToFloat (arr[1]);
         float w = Invariant.ToFloat (arr[2]);
         float h = Invariant.ToFloat (arr[3]);
         return new RectangleF(x*scale, y*scale, w * scale, h * scale);
      }

      public static RotateMode ToBackwardRotate(this RotateMode rm) {
         switch (rm) {
            case RotateMode.Rotate90: return RotateMode.Rotate270;
            case RotateMode.Rotate270: return RotateMode.Rotate90;
            default: return rm;
         }
      }
   }
}

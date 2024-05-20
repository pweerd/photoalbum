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
using Bitmanager.Xml;
using System.Drawing;
using System.Numerics;
using System.Xml;

namespace BMAlbum.Core {

   public class Shrinker {
      public readonly ShrinkOperation[] Operations;
      public readonly int Quality;
      public readonly bool UseCache;
      public Logger Logger;

      public Shrinker (IList<ShrinkOperation> operations, int quality=80, bool useCache=true) {
         Operations = operations.ToArray ();
         Quality = quality;
         UseCache = useCache;
      }
      public Shrinker (XmlNode node) {
         Quality = node.ReadInt ("@quality", 80);
         UseCache = node.ReadBool ("@cache", true);
         if (node.ReadBool("@log", false)) Logger = Logs.CreateLogger ("timings", node.Name);
         const string possible = "fix_orientation, factor2, factor15, exact, sharpen, gausse_sharpen, min_factor";
         var list = new List<ShrinkOperation> ();
         foreach (XmlNode sub in node.ChildNodes) {
            if (sub.NodeType != XmlNodeType.Element) continue;
            ShrinkOperation tmp;
            switch (sub.Name) {
               case "fix_orientation": tmp = new ShrinkOrientationFixer (); break;
               case "factor2": tmp = new ShrinkFactor2 (sub); break;
               case "factor15": tmp = new ShrinkFactor15 (sub); break;
               case "exact": tmp = new ShrinkExact (); break;
               case "sharpen": tmp = new ShrinkSharpen (sub); break;
               case "gausse_sharpen": tmp = new ShrinkGausseSharpen (sub); break;
               case "min_factor": tmp = new CheckMinFactor (sub); break;
               default:
                  if (sub.Name.StartsWith ('_')) continue;
                  throw new BMNodeException (sub, "Unrecognized node [{0}]. Possible values are: {1}.", sub.Name, possible);
            }
            list.Add (tmp);
         }
         Operations = list.ToArray ();
      }

      public bool Shrink (ref Bitmap bm, int targetW, int targetH) {
         var ctx = new ShrinkContext (bm);
         if (Logger != null) {
            for (int i = 0; i < Operations.Length; i++) {
               bool ret;
               string name = Operations[i].GetType ().Name;
               Logger.Log (_LogType.ltTimerStart, name);
               Bitmap tmp = ctx.Bitmap;
               ret=Operations[i].Process (ctx, targetW, targetH);
               Logger.Log (_LogType.ltTimerStop, "{0} -> {1}, changed={2}", name, ret, !object.ReferenceEquals(tmp, ctx.Bitmap));
               if (!ret) break;
            }
         } else {
            for (int i = 0; i < Operations.Length; i++) {
               if (!Operations[i].Process (ctx, targetW, targetH)) break;
            }
         }
         bm = ctx.Bitmap;
         return ctx.CopyPropertiesIfChanged();
      }
      public int GetFingerPrint (Bitmap bm, int targetW, int targetH) {
         int fp = 0;
         if (Logger != null) {
            for (int i = 0; i < Operations.Length; i++) {
               bool ret;
               string name = Operations[i].GetType ().Name;
               Logger.Log (_LogType.ltTimerStart, name);
               ret = Operations[i].GetFingerprint (bm, targetW, targetH, ref fp);
               Logger.Log (_LogType.ltTimerStop, "{0} -> {1}, fp={2}", name, ret, fp);
               if (!ret) break;
            }
         } else {
            for (int i = 0; i < Operations.Length; i++) {
               if (!Operations[i].GetFingerprint (bm, targetW, targetH, ref fp)) break;
            }
         }
         return fp;
      }


   }

}

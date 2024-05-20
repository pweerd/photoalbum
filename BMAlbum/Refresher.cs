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
using Bitmanager.Elastic;
using Bitmanager.Json;
using BMAlbum.Controllers;
using System;
using System.Net.WebSockets;

namespace BMAlbum {

   public class RefreshParams {
      public readonly int MaxCount;
      public readonly int[] Dimensions;
      public readonly int WaitBetweenItems;
      public readonly CacheType Type;
      public RefreshParams (JsonObjectValue v) {
         MaxCount = v.ReadInt ("max_count", int.MaxValue);
         WaitBetweenItems = v.ReadInt ("wait_between_items", 100);
         Type = v.ReadEnum ("cache_type", CacheType.Small);
         Dimensions = asIntArr (v.ReadValue ("dims", true));
         if (Dimensions.Length == 0) throw new BMException ("At least 1 dimension value is needed for 'dims'");
      }

      private static int[] asIntArr (JsonValue v) {
         int[] ret;
         var arr = v as JsonArrayValue;
         if (arr == null) {
            ret = new int[1];
            ret[0] = v.AsInt ();
         } else {
            ret = new int[arr.Count];
            for (int i = 0; i < ret.Length; i++) {
               ret[i] = arr[i].AsInt ();
            }
         }
         return ret;
      }
      public JsonObjectValue ToJson () {
         var ret = new JsonObjectValue ();
         ret["cache_type"] = Type.ToString ();
         ret["max_count"] = MaxCount;
         ret["wait_between_items"] = WaitBetweenItems;
         ret["dims"] = new JsonArrayValue(Dimensions);
         return ret;
      }

      public override string ToString () {
         return Invariant.Format("Refresh [MaxCount={0}, WaitBetweenItems={1}ms]", MaxCount, WaitBetweenItems);
      }
   }

   public class RefreshStats {
      public readonly RefreshParams Params;
      public readonly int Total, TotalIds, Existing;
      public readonly bool Active;

      public RefreshStats (RefreshParams parms, bool active, int total, int totalIds, int existing) {
         Params = parms;
         Total = total;
         TotalIds = totalIds;
         Existing = existing;
         Active = active;
      }

      public JsonObjectValue ToJson () {
         var ret = new JsonObjectValue ();
         ret["active"] = Active;
         ret["total"] = Total;
         ret["total-ids"] = TotalIds;
         ret["existing"] = Existing;
         return ret;
      }
   }

   public class Refresher {
      private readonly Logger logger;
      private readonly Settings settings;
      private readonly AutoResetEvent triggerEvent;

      private volatile bool mustStop, mustStopThread;

      private volatile RefreshParams refreshParms;
      private volatile RefreshStats refreshStats;

      public Refresher (Settings settings) {
         logger = Logs.CreateLogger ("refresher", "refresher");
         this.settings = settings;
         triggerEvent= new AutoResetEvent (false);
         new Thread (threadProc).Start();
      }

      protected static JsonObjectValue createStats (bool active, int total, int totalIds, int existing) {
         var ret = new JsonObjectValue ();
         ret["active"] = active;
         ret["refreshed_total"] = total;
         ret["refreshed_ids"] = totalIds;
         ret["existing"] = existing;
         return ret;
      }

      public RefreshStats GetStats () {
         return refreshStats ?? new RefreshStats (null, false, 0, 0, 0);
      }

      protected virtual void threadProc() {
         mustStopThread = false;
         mustStop = false;
         try {
            logger.Log ("Refresher starting");
            while (true) {
               logger.Log ("Waiting for trigger");
               triggerEvent.WaitOne ();
               mustStop = false;
               var parms = Interlocked.Exchange (ref refreshParms, null);
               logger.Log ("Triggered. Params={0}", parms);
               if (mustStopThread) break;

               if (parms == null) {
                  logger.Log ("Nothing to do: no parms");
                  continue;
               }

               try {
                  runRefresh (parms);
               } catch (Exception ee) {
                  logger.Log (ee, "Error while running refresh: {0}", ee.Message);
               }
               if (mustStopThread) break;
            }
         } catch (Exception e) {
            logger.Log (e, "Stopped due to error.");
         }
         logger.Log ("Refresher stopped");
      }

      public void Trigger (RefreshParams p) {
         logger.Log ("Trigger: {0}", p);
         Interlocked.Exchange (ref refreshParms, p);
         triggerEvent.Set ();
      }

      private void runRefresh(RefreshParams refreshParms) {
         var photoCtr = new PhotoController ();
         photoCtr.setSettings(settings);
         var c = settings.ESClient;
         var req = c.CreateSearchRequest (settings.MainIndex);
         req.SetSource (null, "*");
         req.Sort.Add (new ESSortField ("sort_key", ESSortDirection.desc));
         var records = new ESRecordEnum (req);
         int max = refreshParms.MaxCount;
         if (max <= 0) max = int.MaxValue;
         int existing = 0;
         int total = 0;
         int totalIds = 0;
         int[] requestedDims = refreshParms.Dimensions;
         CacheType type = refreshParms.Type;
         foreach (var doc in records) {
            if (mustStop) break;

            string id = doc.Id;
            if ((total % 100) == 0) refreshStats = new RefreshStats (refreshParms, true, total, totalIds, existing);
            ++totalIds;
            int _existing = existing;
            for (int i=0; i< requestedDims.Length; i++) {
               int dim = requestedDims[i];
               if (mustStop) break;
               logger.Log ("Shrinking to dim={0}: {1}", dim, id);
               ++total;
               if (photoCtr.ShrinkToCache (type, id, dim)) ++existing;
               else logger.Log (_LogType.ltTimer, "Shrinking to h={0}: {1}", dim, id);
            }

            if (mustStop) break;
            if (existing==_existing) {
               if (--max < 0) break;
               if (refreshParms.WaitBetweenItems > 0) Thread.Sleep (refreshParms.WaitBetweenItems);
            }
         }
         logger.Log ("Refesher terminated. Records={0}, done={1}, existing={2}", totalIds, total, existing);
         refreshStats = new RefreshStats (refreshParms, false, total, totalIds, existing);
      }

      public void StopRefresh () {
         mustStop = true;
         triggerEvent.Set ();
      }
      public void StopThread () {
         mustStopThread = true;
         mustStop = true;
         triggerEvent.Set ();
      }


   }
}

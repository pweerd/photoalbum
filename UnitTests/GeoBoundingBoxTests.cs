/*
 * Licensed to De Bitmanager under one or more contributor
 * license agreements. See the NOTICE file distributed with
 * this work for additional information regarding copyright
 * ownership. De Bitmanager licenses this file to you under
 * the Apache License, Version 2.0 (the "License"); you may
 * not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *    http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing,
 * software distributed under the License is distributed on an
 * "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY
 * KIND, either express or implied.  See the License for the
 * specific language governing permissions and limitations
 * under the License.
 */

using Bitmanager.Core;
using Bitmanager.IO;
using Bitmanager.Test;
using BMAlbum.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Text;

namespace UnitTests {
   [TestClass]
   public class GeoBoundingBoxTests : TestBase { 

      [TestMethod]
      public void TestBox () {
         var box = new GeoBoundingBox ("55.53585,4.251394,51.16667,8.371267");
         Assert.AreEqual("55.53585,4.251394,51.16667,8.371267", box.ToString());

         var box2 = box.Zoom (3);
         Assert.AreEqual ("59.90503,0.13152027,46.797493,12.491141", box2.ToString ());

         Assert.AreEqual (0, box2.GetPartIndex ("46.797493,0.13152027", 3));
         Assert.AreEqual (0, box2.GetPartIndex ("47,0.14", 3));
         Assert.AreEqual (1, box2.GetPartIndex ("47,5", 3));
         Assert.AreEqual (2, box2.GetPartIndex ("47,9", 3));
         Assert.AreEqual (3, box2.GetPartIndex ("52,0.14", 3));
         Assert.AreEqual (4, box2.GetPartIndex ("52,5", 3));
         Assert.AreEqual (5, box2.GetPartIndex ("52,9", 3));
         Assert.AreEqual (6, box2.GetPartIndex ("56,0.14", 3));
         Assert.AreEqual (7, box2.GetPartIndex ("56,5", 3));
         Assert.AreEqual (8, box2.GetPartIndex ("56,9", 3));
         Assert.AreEqual (6, box2.GetPartIndex ("59.90503,0.13152027", 3));
         Assert.AreEqual (8, box2.GetPartIndex ("59.90503,12.491141", 3));
         Assert.AreEqual (8, box2.GetPartIndex ("60,13", 3));

         Assert.AreEqual ("51.16667,0.13152027,46.797493,4.251394", box2.GetPart (0, 3).ToString ());
         Assert.AreEqual ("51.16667,4.251394,46.797493,8.371267",   box2.GetPart (1, 3).ToString ());
         Assert.AreEqual ("51.16667,8.371267,46.797493,12.49114",   box2.GetPart (2, 3).ToString ());
         Assert.AreEqual ("55.53585,0.13152027,51.16667,4.251394",  box2.GetPart (3, 3).ToString ());
         Assert.AreEqual ("55.53585,4.251394,51.16667,8.371267", box2.GetPart (4, 3).ToString ());
         Assert.AreEqual ("55.53585,8.371267,51.16667,12.49114", box2.GetPart (5, 3).ToString ());
         Assert.AreEqual ("59.90503,0.13152027,55.53585,4.251394", box2.GetPart (6, 3).ToString ());
         Assert.AreEqual ("59.90503,4.251394,55.53585,8.371267", box2.GetPart (7, 3).ToString ());
         Assert.AreEqual ("59.90503,8.371267,55.53585,12.49114", box2.GetPart (8, 3).ToString ());

      }
   }
}

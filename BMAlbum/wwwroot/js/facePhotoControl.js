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

function createFacePhotoControl(app) {
   const MAX = 32000.0;
   const _zoomer = createSingleZoomer($("#facesphoto"));
   let _faceRect;

   function _onLoad(ev) {
      if (!_faceRect) return;
      _zoomer.reset();

      const rc = ev.target.getBoundingClientRect();
      const factor = Math.max(rc.width, rc.height) / MAX;
      //console.log('rc:', rc, 'relpos', _faceRect);
      const x1 = factor * _faceRect[0] + rc.left - 4;
      const x2 = factor * _faceRect[1] + rc.left + 4;
      const y1 = factor * _faceRect[2] + rc.top-4;
      const y2 = factor * _faceRect[3] + rc.top+4; 
      //console.log('coords:', x1, y1, x2-x1, y2-y1);

      const $face = $("#facerect");
      $face.css('top', y1)
         .css('left', x1)
         .width(x2 - x1)
         .height(y2 - y1)
         .removeClass("hidden");
   }

   function _onPopHistory(ev) {
      console.log('HISTORY popped:', history.length, history.state, ev);
      if (!history.state) return;

      const url = history.state.url;
      const rc = history.state.rc;
      if (url) _setPhoto(url, history.state.rc, true);
   }


   function _setPhoto(url, rect, fromHistory) {
      const $c = $("#facesphoto");
      const $img = $c.find("img");
      $("#facerect").addClass("hidden");
      _faceRect = rect;
      let idx1 = url.indexOf('&id=')+4;
      let idx2 = url.lastIndexOf('.');
      let id = decodeURIComponent(url.substring(idx1, idx2));
      $("#faceshdr").text(id);
      $img.attr("src", url);

      document.title = id + " | Gezichten"; 

      //if (!fromHistory) {
      //   const histState = { mode:"facephoto", url: url, rc: rect };
      //   let fnHist = history.pushState;
      //   //if (!history.state) fnHist = history.replaceState;

      //   fnHist.call(history, histState, '', location.href);
      //}
   }

   $("#facesphoto img").on("load", _onLoad);
   window.addEventListener('keydown', function (ev) {
      return _zoomer.onKeyDown(ev);
   });
   //window.addEventListener('popstate', _onPopHistory);

   return {
      setPhoto: _setPhoto,
      onPopHistory: _onPopHistory,
   };
}


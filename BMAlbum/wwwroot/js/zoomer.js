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

function createZoomer($elt) {
   let _logScale = 0;
   let _offsetX = 0;
   let _offsetY = 0;
   let _initial = {};
   let _maxScale;
   let _photos;
   let _index = -1;

   function _bounded(v) {
      if (v < -100) v = -100;
      else if (v > 100) v = 100;
      return v; 
   }

   function _setLogScale(s) {
      let $p = $elt.closest('.lg-outer');
      if (s <= 0) {
         _logScale = 0;
         //$p.removeClass('lg-zoomed');
      } else {
         _logScale = s > 5 ? 5 : s;
         //$p.addClass('lg-zoomed');
      }
   }
   function _onKeyDown(ev) {
      if ((ev.key === '+' || ev.key === '-') && ev.ctrlKey) {
         let $cur = $elt.find('.lg-current');
         if (_initScrol($cur.find('.lg-image'))) {
            _setLogScale (_logScale + (ev.key === '+' ? 1 : -1));
            ev.originalEvent.stopImmediatePropagation();
            ev.preventDefault();
            _applyScaleAndOffset();
         }
         return;
      }

      if (_index >= 0 && _photos) {

         switch (ev.code) {
            default: return;
            case 'Escape':
               if (_logScale === 0) return;
               _offsetX = 0;
               _offsetY = 0;
               _setLogScale(0);
               break;
            case 'ArrowLeft':
               _offsetX = _bounded(_offsetX - 10);
               break;
            case 'ArrowUp':
               _offsetY = _bounded(_offsetY - 10);
               break;
            case 'ArrowRight':
               _offsetX = _bounded(_offsetX + 10);
               break;
            case 'ArrowDown':
               _offsetY = _bounded(_offsetY + 10);
               break;
         } 
         ev.originalEvent.stopImmediatePropagation();
         ev.preventDefault();
         _applyScaleAndOffset();
      } 
   }

   function _applyScaleAndOffset() {
      console.log('logScale=', _logScale, 1.4 ** _logScale, _index);
      if (_index < 0 ) return;

      let scale = Math.min(1.4 ** _logScale, _maxScale);
      console.log('Scale=', scale, 1.4 ** _logScale, _maxScale);

      let p = $elt[0];
      let pw = Math.max(0, _initial.w * scale - p.clientWidth);
      let ph = Math.max(0, _initial.h * scale - p.clientHeight);
      let x = Math.round((_offsetX / 200.0) * pw / scale);
      let y = Math.round((_offsetY / 200.0) * ph / scale);
      console.log('top=', p.scrollTop, ', ph=', ph, ', offsetY=', _offsetY, y, ',h=', _initial.h, _initial.h * scale, p.clientHeight);
      let sb = ['scale('];
      sb.push(scale);
      sb.push(') translate(');
      sb.push(x);
      sb.push('px, ');
      sb.push(y);
      sb.push('px)');
      console.log('transform', sb.join(''));
      $elt.find('.lg-current').find('img').css('transform', sb.join(''));
   }

   function _initScrol($img) {
      if ($img.length === 0) return false;
      let ix = 0 + $img.data('index');
      console.log('zoom:ix', ix, _index);
      //Handle index change
      if (ix !== _index) {
         _index = ix;
         let w = $img[0].scrollWidth;
         let h = $img[0].scrollHeight;
         _initial = {
            w: w,
            h: h
         };
         _maxScale = Math.max(_photos[ix].w / $elt[0].clientWidth, _photos[ix].h / $elt[0].clientHeight);
      }
      return true;
   }
   function _onMouseWheel(ev) {
      ev.preventDefault();
      let $target = $(ev.target).closest('.lg-inner');
      let $img = $target.find('.lg-current').find('.lg-image');
      if ($img.length === 0) return;

      _initScrol($img);

      let e = ev.originalEvent;

      //Handle ctrl-wheel -> zoom
      if (ev.ctrlKey) {
         if (e.deltaY > 0) _setLogScale(_logScale - 1);
         else if (e.deltaY < 0) _setLogScale(_logScale + 1);
         else return;
         _applyScaleAndOffset();
         return;
      }

      //Handle wheel -> scroll
      if (e.deltaY > 0) _offsetY = _bounded(_offsetY - 10);
      else if (e.deltaY < 0) _offsetY = _bounded(_offsetY + 10);
      else if (e.deltaX > 0) _offsetX = _bounded(_offsetX - 10);
      else if (e.deltaX < 0) _offsetX = _bounded(_offsetX + 10);
      else return;
      ev.preventDefault();
      _applyScaleAndOffset();
   }

   function _reset() {
      _setLogScale(0);
      _offsetX = 0;
      _offsetY = 0;
      _index = -1;
      $elt.find('img').css('transform', '');
   }

   function _setPhotos(photos) {
      _photos = photos;
      _reset();
   }

   $elt.on('wheel', _onMouseWheel);

   return {
      reset: _reset,
      setPhotos: _setPhotos,
      onKeyDown: _onKeyDown
   }
}
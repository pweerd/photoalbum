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
            _setLogScale(_logScale + (ev.key === '+' ? 1 : -1));
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
      if (_index < 0) return;

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

function createSingleZoomer($elt) {
   let _logScale;
   let _maxScale;
   let _scale;
   let _centerX;
   let _centerY;

   function _setLogScale(s) {
      s = Math.round(s);
      if (s <= 0) return _reset();
      if (s > 7) s = 7;    //leads to scale ~ 10

      let scale = 1.4 ** s;
      if (_maxScale && scale > _maxScale) return;

      _logScale = s;
      _scale = scale;
   }

   function _onKeyDown(ev) {
      if ("input" === ev.target.localName) return;
      if (ev.ctrlKey) {
         if ((ev.key === '+' || ev.key === '-')) {
            _setLogScale(_logScale + (ev.key === '+' ? 1 : -1));
            _applyScaleAndOffset();
            ev.stopImmediatePropagation();
            ev.preventDefault();
         }
         return;
      }

      let dx = 0, dy = 0;
      switch (ev.code) {
         default: return;
         case 'Escape':
            if (_logScale !== 0) _reset();
            break;
         case 'ArrowLeft': dx = -10; break;
         case 'ArrowRight': dx = 10; break;
         case 'ArrowUp': dy = -10; break;
         case 'ArrowDown': dy = 10; break;
      }
      if (_logScale !== 0) {
         _centerX += dx;
         _centerY += dy;
         _applyScaleAndOffset();
      }
      ev.stopImmediatePropagation();
      ev.preventDefault();
   }

   function _bounded(v, lower, upper) {
      if (v < lower) return lower;
      if (v > upper) return upper;
      return v;
   }
   function _applyScaleAndOffset() {
     // console.log('logScale=', _logScale, 1.4 ** _logScale);

      const halfW = $elt.width() /2;
      const halfH = $elt.height() / 2;
      let maxDelta;

      maxDelta = halfW * (1 - 1/ _scale);
      _centerX = _bounded(_centerX, halfW-maxDelta, halfW+maxDelta);
      maxDelta = halfH * (1 - 1 / _scale);
      _centerY = _bounded(_centerY, halfH-maxDelta, halfH+maxDelta);

      let sb = ['scale('];
      sb.push(_scale);
      sb.push(') translate(');
      sb.push(_centerX - halfW);
      sb.push('px, ');
      sb.push(_centerY - halfH);
      sb.push('px)');
      console.log('transform', sb.join(''));
      $elt.css('transform', sb.join(''));
   }


   let _x0, y0;
   let _cx0, _cy0;

   function _onMouseDown(ev) {
      if (ev.buttons !== 1 || _logScale === 0) return; //Not for us

      _cx0 = _centerX;
      _cy0 = _centerY;
      _x0 = ev.clientX;
      _y0 = ev.clientY;

      $elt[0].addEventListener('mousemove', _onMouseMove, true);
      ev.preventDefault();
      ev.stopImmediatePropagation();
   }

   function _onMouseMove(ev) {
      ev.preventDefault();
      ev.stopImmediatePropagation();

      if (_logScale > 0) {
         _centerX = _cx0 + (ev.clientX - _x0) / _scale;
         _centerY = _cy0 + (ev.clientY - _y0) / _scale;
         _applyScaleAndOffset();
      }
   }

   function _onMouseUp(ev) {
      $elt[0].removeEventListener('mousemove', _onMouseMove, true);
      if (_logScale !== 0) {
         ev.preventDefault();
         ev.stopImmediatePropagation();
      }
   }

   function _ignoreOnNotScaled(ev) {
      console.log('ignored if scaled: ', _logScale, ev);
      if (_logScale > 0) {
         ev.preventDefault();
         ev.stopImmediatePropagation();
      }
   }


   function _onMouseWheel(ev) {
      ev.preventDefault();

      //Handle ctrl-wheel -> zoom
      if (ev.ctrlKey) {
         if (ev.deltaY > 0) _setLogScale(_logScale - 1);
         else if (ev.deltaY < 0) _setLogScale(_logScale + 1);
         else return;
         _applyScaleAndOffset();
         return;
      }

      //Handle wheel -> scroll
      if (_logScale > 0) {
         if (ev.deltaY > 0) _centerY -= 10;
         else if (ev.deltaY < 0) _centerY += 10;
         else if (ev.deltaX > 0) _centerX -= 10;
         else if (ev.deltaX < 0) _centerX += 10;
         else return;
         ev.preventDefault();
         _applyScaleAndOffset();
      }
   }

   function _reset() {
      _logScale = 0;
      _scale = 1;
      _centerX = $elt.width() / 2;
      _centerY = $elt.height() / 2;
      $elt.css('transform', '');
   }

   $elt[0].addEventListener('wheel', _onMouseWheel);
   $elt[0].addEventListener('mousedown', _onMouseDown);
   $elt[0].addEventListener('mouseup', _onMouseUp);
   $elt[0].addEventListener('keydown', _onKeyDown);
   $elt[0].addEventListener('click', _ignoreOnNotScaled);
   $elt[0].addEventListener('dragstart', _ignoreOnNotScaled);
   _reset();

   return {
      reset: _reset,
      onKeyDown: _onKeyDown,
      setMaxScale: function (m) {
         _maxScale = m;
      }
   }
}
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

function createOverlay(pane) {
   const DBG_NO_HIDE = false;   //Keeps the overlay intact if true
   const DBG_DEFAULT = false;   //Default debug value for behaviors
   const OVL_HIDDEN = 'ovl-hidden';
   const OVL_SCROLL = 'ovl-scroll';
   const $overlay = (pane instanceof jQuery) ? pane : $(pane);
   $overlay.addClass(OVL_HIDDEN).addClass('ovl').removeClass('hidden');
   let _target = null;
   let _transparentBackGround;
   let _this = null;
   let _content = null;
   let _state = null;
   let _activationTimer, _hideTimer;
   let _disabledUntil = 0;


   const _overlayBehavior = {
      mode:'overlay',
      debug: DBG_DEFAULT,
      maxHStrategy: undefined,
      maxWStrategy: undefined,
      propagateClick: true,
      closeOnClick: true,
      closeOnWheel: true,
      useTargetsBackground: false,
      initialState: 'html',
      states: ['html', 'text', 'json', 'xml'],
      copyFont: true,
      copyContent: function ($dst, $src) {
         let tmp = $src.html();
         $dst.html(tmp);
         return tmp;
      },
      applyExtraStyles: undefined,
      toggleState: function _toggleState(state) {
         if (state === 'fixed') return state;
         states = this.states;
         ix = (states.indexOf(state) + 1) % states.length;
         return states[ix];
      },
      createPositionParms: function ($div, $target) {
         let ovlOffsetY = _getBorderAndPaddingY($overlay);
         let offsetX = _getBorderAndPaddingX($overlay) - _getBorderAndPaddingX($target);
         let offsetY = ovlOffsetY - _getBorderAndPaddingY($target);
         if (!this.ignoreInnerPadding) {
            offsetX += _getBorderAndPaddingX($div);
            offsetY += _getBorderAndPaddingY($div);
         }
         return {
            my: "left top",
            at: "left-" + offsetX + "px top-" + offsetY + "px",
            of: $target,
            collision: this.collision ?? "fit",
         };
      },
      needShow: function ($target) {
         return !_doesFit($target);
      },
      showState: function (state) {
         let ret = true;
         switch (state) {
            default: _asText(); break;
            case 'html': _asHtml(); break;
            case 'json': ret = _asJson(); break;
            case 'xml': ret = _asXml(); break;
            case 'fixed': break;
         }
         console.log('showState(', state, ')-->', ret);
         return ret;
      },
      _isExtended: true,
      initialClass: $overlay.attr('class'),
   };

   const _tooltipBehavior = $.extend({}, _overlayBehavior, {
      mode: 'tooltip',
      initialState: 'fixed',
      closeOnWheel: false,
      applyExtraStyles: 'ovl-smooth',

      createPositionParms: function ($div, $target) {
         return {
            my: "left top",
            at: "left+2px bottom+2px",
            of: $target,
            collision: this.collision ?? "fit",
         };
      },
      needShow: function ($target) {
         return $target.attr('data-title');
      },
      copyContent: function ($dst, $src) {
         this.showState('html');
         let title = $src.attr('data-title');
         $dst.text(title);
         return title;
      }
   });

   const _scrollBehavior = $.extend({}, _overlayBehavior, {
      mode: 'scroll',
      closeOnWheel: 'if_needed',
      closeOnClick: true
   });

   function _setDefaultBehaviorProp(k, v) {
      _overlayBehavior[k] = v;
      _scrollBehavior[k] = v;
      _tooltipBehavior[k] = v;
   }

   function _createBehaviorFrom(behavior, from) {
      const ret = $.extend({}, from, behavior);
      ret._isExtended = true;

      if (behavior && behavior.initialClass) {
         let classes = ret.initialClass ?? '';
         let arr = (from.initialClass + " " + behavior.initialClass).split(' +');
         let dst = [];
         for (let i = 0; i < arr.length; i++) {
            let c = arr[i];
            if (c === '-') {
               const index = dst.indexOf(c.substring(1));
               if (index >= 0) dst.splice(index, 1);
               continue;
            }
            if (c === '+') {
               const cls = c.substring(1);
               const index = dst.indexOf(cls);
               if (index < 0) dst.push(cls);
               continue;
            }
            dst.push(c);
         }
         ret.initialClass = dst.join(' ');
      }
      return ret;
   }

   function _createBehavior(behavior, from) {
      let ret = behavior;
      if (!behavior || !behavior._isExtended) {
         let _def = _overlayBehavior;
         if (behavior !== null && typeof behavior === 'object' && !Array.isArray(behavior)) {
            switch (behavior.mode) {
               case 'scroll': _def = _scrollBehavior; break;
               case 'tooltip': _def = _tooltipBehavior; break;
            }
         }
         ret = _createBehaviorFrom(behavior, _def);
      }
      return ret;
   }

   let _behavior = _overlayBehavior;

   function _cssAsFloat($elt, prop) {
      return parseFloat($elt.css(prop))
   }
   function _getBorderAndPaddingX($elt) {
      return _cssAsFloat($elt, "padding-left") + _cssAsFloat($elt,"border-left-width");
   }
   function _getBorderAndPaddingY($elt) {
      return _cssAsFloat($elt, "padding-top") + _cssAsFloat($elt,"border-top-width");
   }

   function _insertDiv() {
      $overlay.html("<div tabindex='-1'></div>");
      return $overlay.find("div");
   }

   function _clearActivationTimer() {
      if (_activationTimer !== undefined) {
         clearTimeout(_activationTimer);
         _activationTimer = undefined;
      }
   }
   function _clearHideTimer() {
      if (_hideTimer !== undefined) {
         clearTimeout(_hideTimer);
         _hideTimer = undefined;
      }
   }

   function _hide() {
      //We postpone the real hide a bit, so that we can cancel the hide if it is followed by a (re-)activation
      if (_hideTimer !== undefined) return;
      _clearActivationTimer();
      let t;
      _hideTimer = t = setTimeout(function (e) {
         _hideTimer = undefined;
         _hideNow();
      }, 50);
   }

   function _hideNow() {
      _clearActivationTimer();
      if (!DBG_NO_HIDE) {
         $overlay.addClass(OVL_HIDDEN);
         if (!_behavior.debug) $overlay.html('');
      }
      _behavior = _overlayBehavior;
      _content = undefined;
      _target = undefined;
   }

   function _scrollNeeded() {
      const ovl = $overlay[0];
      return (ovl.scrollHeight > ovl.clientHeight) || (ovl.scrollWidth > ovl.clientWidth);
   }

   function _activate(target, behavior, argDelay) {
      _clearActivationTimer();
      const dbg = behavior && behavior.debug;

      $target = (target instanceof jQuery) ? target : $(target);
      if (dbg) console.log("OVL activate target=", typeof $target, $target);
      if ($target.length === 0) return;
      target = $target[0];
      behavior = _createBehavior(behavior);

      //Early out if we already had a visible overlay
      if (!$overlay.hasClass(OVL_HIDDEN)) return _activateNow(behavior, $target);

      let delay = argDelay === undefined ? behavior.delay : argDelay;
      if (delay < 20) delay = 20;

      //Save activation position to check if there was a scroll during the timer
      const rect = target.getBoundingClientRect();
      //Save if we had the mouse on activation
      const hadMouse = target.matches(':hover');

      if (_activationTimer !== undefined) return;
      _activationTimer = setTimeout(function () {
         if (!target.matches(':hover') && hadMouse) {
            return console.log("NOT activated: mouse left target");
         }
         const rc2 = target.getBoundingClientRect();
         if (rect.left !== rc2.left || rect.top !== rc2.top) {
            return console.log("NOT activated: target moved");
         }
         _activateNow(behavior, $target);
      }, delay);
   }
   function _activateNow(behavior, $target) {
      const dbg = behavior.debug;

      _clearActivationTimer();
      _clearHideTimer();

      if (Date.now() < _disabledUntil) {
         console.log("NOT activated: temp disabled");
         return;
      }
      if (_target && _target[0] === $target[0]) {
         console.log("NOT activated: already active");
         return;
      }

      _target = $target;
      _behavior = behavior;
      const $div = _insertDiv();
      _state = behavior.initialState;


      if (behavior.closeOnWheel) {
         //Make sure that we are hidden when a wheel event occurs.
         //Reason is that the div below us should handle the event in case of a non-scrolling overlay
         $div[0].addEventListener("wheel", function (ev) {
            if (behavior.closeOnWheel !== true) {
               const scrollNeeded = _scrollNeeded();
               //console.log('Checking scrollNeeded (', scrollNeeded, ') since closeOnWheel=', behavior.closeOnWheel);
               if (scrollNeeded) return;
            }

            const tgt = _target[0]; //will be reset by hideNow()
            _disabledUntil = 200 + Date.now();
            _hideNow();
            for (let p = tgt; p; p = p.parentNode) {
               if (p.scrollHeight > p.clientHeight) {
                  console.log('scroll: ', p);
                  p.scrollTop = p.scrollTop + ev.deltaY;
                  break;
               }
            }
         }, { passive: true });
      }

      $overlay.attr('class', behavior.initialClass);
      if (behavior.mode === "scroll" || behavior.mode === "tooltip") {
         if (dbg) console.log("OVL: Need largeTooltip");
         $overlay.addClass(OVL_SCROLL);
         if (behavior.copyFont) _copyFont($div, $target);
         _content = "" + behavior.copyContent($div, $target);
         _state = _convertAutoState(_state);
         if (_content && !behavior.showState(_state)) behavior.showState(_state='text');
      } else {
         $div.css('min-height', ($target.innerHeight() + 3)+'px');
         if (!behavior.useTargetsBackground) {
            if (dbg) console.log("OVL: own background");
            if (behavior.copyFont) _copyFont($div, $target);
            behavior.copyContent($div, $target);
         } else {
            if (dbg) console.log("OVL: target's background");
            const $clone = $target.clone();

            if (behavior.copyFont) _copyFont($clone, $target);
            $clone.css("position", "static");
            $div.copyStyles($target.parent(), "^color|^background|^cursor");
            _copyStyleDeep($div, $target.parent(), 'background-color', _transparentBackGround);

            $clone.copyStyles($target, "^color|^background|^cursor|^line");
            _copyStyle($clone, $target, 'background-color');
            $clone.css('border-color', _transparentBackGround);
            $div.append($clone);
         }
      }


      if (behavior.applyExtraStyles) {
         if ("function" === typeof behavior.applyExtraStyles) behavior.applyExtraStyles($div);
         else $div.addClass(behavior.applyExtraStyles);
      }
      _reposition($div, $target);
   }

   const rhorizontal = /left|center|right/;
   const rvertical = /top|center|bottom/;
   const roffset = /[\+\-]\d+(\.[\d]+)?%?/;
   const rposition = /^\w+/;
   const rpercent = /%$/;
   function _getBasePosition(options, rc) {
      // Make a copy, we don't want to modify arguments
      options = $.extend({}, options);
      const offsets = {};

      // Force my and at to have valid horizontal and vertical positions
      // if a value is missing or invalid, it will be converted to center
      $.each(["my", "at"], function () {
         let pos = (options[this] || "").split(" "),
            horizontalOffset,
            verticalOffset;

         if (pos.length === 1) {
            pos = rhorizontal.test(pos[0]) ?
               pos.concat(["center"]) :
               rvertical.test(pos[0]) ?
                  ["center"].concat(pos) :
                  ["center", "center"];
         }
         pos[0] = rhorizontal.test(pos[0]) ? pos[0] : "center";
         pos[1] = rvertical.test(pos[1]) ? pos[1] : "center";

         // Calculate offsets
         horizontalOffset = roffset.exec(pos[0]);
         verticalOffset = roffset.exec(pos[1]);
         offsets[this] = [
            horizontalOffset ? horizontalOffset[0] : 0,
            verticalOffset ? verticalOffset[0] : 0
         ];

         // Reduce to just the positions without the offsets
         options[this] = [
            rposition.exec(pos[0])[0],
            rposition.exec(pos[1])[0]
         ];
      });

      function getOffsets(offsets, width, height) {
         return [
            parseFloat(offsets[0]) * (rpercent.test(offsets[0]) ? width / 100 : 1),
            parseFloat(offsets[1]) * (rpercent.test(offsets[1]) ? height / 100 : 1)
         ];
      }

      let baseTop = rc.top;
      let baseLeft = rc.left;

      if (options.at[0] === "right") {
         baseLeft += rc.width;
      } else if (options.at[0] === "center") {
         baseLeft += rc.width / 2;
      }

      if (options.at[1] === "bottom") {
         baseTop += rc.height;
      } else if (options.at[1] === "center") {
         baseTop += rc.height / 2;
      }

      const atOffset = getOffsets(offsets.at, rc.width, rc.height);
      return [baseLeft + atOffset[0], baseTop + atOffset[1]];
   }

   // Reposition the overlay in 2 steps:
   // - first position
   // - apply max-height/width based on the position
   // - position another time
   function _reposition($div, $target) {
      if (!$div) $div = $overlay.find("div");
      const parms = _behavior.createPositionParms($div, $target);
      const allowFlip = (parms.collision ?? '').includes('flip');
      //console.log("OVL: parms=", parms, ', tgt=', $target[0]);

      const ovl = $overlay[0];
      const targetRC = $target[0].getBoundingClientRect();
      const basePos = _getBasePosition(parms, targetRC);
      console.log("target : ", targetRC.left, targetRC.top);
      const ovlLeft = basePos[0];
      const ovlTop = basePos[1];
      console.log("overlay: ", ovlLeft, ovlTop);
      let maxW, maxH, top, left;

      switch (typeof _behavior.maxWStrategy) {
         default: break;
         case "function":
            _behavior.maxWStrategy($overlay, $target);
            break;
         case "string":
            console.log("maxWStrategy=", _behavior.maxWStrategy);
            switch (_behavior.maxWStrategy) {
               case '':
               case 'none':
                  maxW = '';
                  break;
               case 'window':
                  maxW = (allowFlip ? Math.max(ovlLeft, window.innerWidth - ovlLeft) : (window.innerWidth - ovlLeft)) + 'px';
                  break;
               case 'default':
                  break;
               default:
                  maxW = _behavior.maxWStrategy;
                  break;
            }
            break;
      }

      switch (typeof _behavior.maxHStrategy) {
         default: break;
         case "function":
            _behavior.maxHStrategy($overlay, $target);
            break;
         case "string":
            console.log("maxHStrategy=", _behavior.maxHStrategy);
            switch (_behavior.maxHStrategy) {
               case 'none':
                  maxH = '';
                  break;
               case 'target':
               case 'overlay': alert('Not supported: [' + _behavior.maxHStrategy + ']'); break;
               case 'window':
                  maxH = (allowFlip ? Math.max(ovlTop, window.innerHeight - ovlTop) : (window.innerHeight - ovlTop)) + 'px';
                  break;
               default:
                  maxH = _behavior.maxHStrategy;
                  break;
            }
            break;
      }

      if (maxW !== undefined) ovl.style.maxWidth = maxW;
      if (maxH !== undefined) ovl.style.maxHeight = maxH;

      $overlay.position(parms).removeClass(OVL_HIDDEN);
      $div.focus();
   }

   function _getStyleDeep($elt, name, notAllowedValue) {
      let ret;
      let n = 0;
      for (let e = $elt; e; e = e.parent()) {
         n++;
         ret = e.css(name);
         console.log("--tmp", name, ":", ret);
         if (!ret || ret.length === 0) continue;
         if (ret !== notAllowedValue) break;
      }
      console.log("getstyle", name, ":", ret, n);
      return ret;
   }
   function _copyStyleDeep($dst, $src, name, notAllowedValue) {
      $dst.css(name, _getStyleDeep($src, name, notAllowedValue));
   }
   function _copyStyle($dst, $src, name) {
      $dst.css(name, $src.css(name));
   }

   function _convertAutoState(state) {
      if (!state.startsWith('auto')) return state;

      if (_content) {
         let nJsonMandatory = 0, nJson=0, nXmlMandatory=0, nXml = 0, nOther = 2, nLF=0, N = _content.length;
         if (N > 2048) N = 2048;
         for (i = 0; i < N; i++) {
            switch (_content[i]) {
               case '[':
               case ']':
               case '{':
               case '}': ++nJsonMandatory; continue;
               case ':': ++nJson; continue;
               case '=': ++nXml; continue;
               case '<':
               case '>': ++nXmlMandatory; continue;
               case ';': nOther += 2; ++nXml; continue;
               case ',': nOther += 2; ++nJson; continue;
               case '\n': nLF++; nOther += 2; ++nXml; ++nJson; continue;
            }
         }
         nJson += nJsonMandatory;
         nXml += nXmlMandatory;
         console.log('_convertAutoState: json=', nJson, nJsonMandatory, 'xml=', nXml, nXmlMandatory, 'LF=', nLF, 'other=', nOther);
         if (nJsonMandatory >= 2 && nJson > nXml + 2 && nJson > nOther) return "json";
         if (nXmlMandatory >= 2 && nXml > nJson + 2 && nXml > nOther) return "xml";
         if (nLF > nXmlMandatory) return "text";
      }
      return state.indexOf('html') >= 0 ? "html" : "text";
   }

   function _insertPRE() {
      $overlay.find("div").html("<pre></pre>");
      return $overlay.find("pre");
   }
   function _asHtml() {
      $overlay.find("div").html(_content);
      return true;
   }
   function _asText() {
      _insertPRE().text(_content);
      return true;
   }
   function _asJson() {
      let ixobj1 = _content.indexOf('{');
      let ixobj2 = _content.lastIndexOf('}');
      let ixarr1 = _content.indexOf('[');
      let ixarr2 = _content.lastIndexOf(']');
      let ix1 = -1, ix2;
      if (ixobj1 >= 0 && ixobj2 > ixobj1) {
         ix1 = ixobj1;
         ix2 = ixobj2;
         if (ixarr1 >= 0 && ixarr2 > ixarr1) {
            if (ixarr1 < ixobj1) {
               ix1 = ixarr1;
               ix2 = ixarr2;
            }
         }
      } else if (ixarr1 >= 0 && ixarr2 > ixarr1) {
         ix1 = ixarr1;
         ix2 = ixarr2;
      }
      if (ix1 < 0 || ix2 <= ix1 + 3) return false;

      let txt = [];
      try {
         let tmp = vkbeautify.json(_content.substr(ix1, ix2 + 1 - ix1), 3);
         if (ix1 > 0) txt.push(_content.substr(0, ix1));
         txt.push(tmp);
         txt.push(_content.substr(ix2 + 1));
      } catch (ex) {
         return false;
      }
      _insertPRE().text(txt.join(''));
      return true;
   }
   function _asXml() {
      let ix1 = _content.indexOf('<');
      let ix2 = _content.lastIndexOf('>');
      if (ix1 < 0 || ix2 <= ix1 + 3) return false;

      let txt = [];
      try {
         let tmp = vkbeautify.xml(_content.substr(ix1, ix2 + 1 - ix1), 3);
         if (ix1 > 0) txt.push(_content.substr(0, ix1));
         txt.push(tmp);
         txt.push(_content.substr(ix2 + 1));
      } catch (ex) {
         return false;;
      }
      _insertPRE().text(txt.join(''));
      return true;
   }

   $overlay.on('mouseleave', function (ev) {
      if (_behavior.debug) console.log("OVL:mouseleave ", ev);
      ev.stopPropagation();
      if (ev.ctrlKey) return;
      _hide();
   }).on('mouseenter', function (ev) {
      if (!_behavior || !_behavior.skipHideIfMouse) return;
      _clearHideTimer();
   }).on('click', function (ev) {
      if (_behavior.debug) console.log('OVL: CLICK', ev);
      if (!ev.altKey) {
         if (_behavior.propagateClick) _propagate(ev, 'click');
         if (_behavior.closeOnClick) {
            _hideNow();
            ev.preventDefault();
            ev.stopPropagation();
         }
         return;
      }
      //For us... we only process alt-click
      _toggleState();
      _reposition($overlay.find("div"), _target);
   });


   function _propagate(ev, type) {
      console.log('OVL: propagate click, target=', _target, ev);
      if (!_target) return;

      function _triggerEvent(ev, tgt) {
         ev.target = tgt;
         ev.currentTarget = tgt;
         ev.delegateTarget = tgt;
         console.log('propagating: ', ev, ' tgt=', tgt);
         $(tgt).trigger(ev);
      }

      let $myInputs = $overlay.children().find(ev.target.nodeName); //get the nodename under TTOverlay (the only child in #overlay)
      let $theirInputs = _target.find(ev.target.nodeName);
      if ($myInputs.length === $theirInputs.length) {
         console.log('OVL: lists are equal:', $myInputs.length);
         for (i = 0; i < $myInputs.length; i++) {
            if ($myInputs[i] !== ev.target) continue;
            _triggerEvent(ev, $theirInputs[i]);
            return;
         }
      }
      console.log('OVL: no equal org and ovl found.', $myInputs, $theirInputs);

      _triggerEvent(ev, _target[0]);
   }
   function _copyFont($dst, $src) {
      //Copy the font. Note that the fontsize could be zoomed!!
      _copyStyle($dst, $src, 'font-family');
      const fs = $src.css("font-size");
      $dst.css("font-size", fs);
      const fs2 = $dst.css("font-size");
      if (fs !== fs2) {
         fsAsInt = parseInt(fs, 10);
         ratio = parseInt(fs2, 10) / fsAsInt;
         $dst.css("font-size", (fsAsInt / ratio) + "px");
      }
   }

   function _doesFit (target, fnStop) {
      let t = target;
      if (target instanceof jQuery) {
         if (target.length === 0) return true;
         t = target[0];
      }
      //console.log('Check fit', t);
      if (!fnStop) fnStop = function (node, cnt) { return cnt > 0; };
      const top = document;
      for (let i = 0; t !== top && !fnStop(t, i); t = t.parentNode, i++) {
         if (t.scrollWidth > t.offsetWidth + 2) {
            //console.log('NOFIT', t.scrollWidth - t.offsetWidth, name, t);
            return false;
         }
      }
      //console.log('FIT');
      return true;
   }

   function _toggleState() {
      let first = _behavior.toggleState(_state);
      console.log('First next state=', first);
      let state = first;
      while (true) {
         console.log('-- Try state', state);
         if (_behavior.showState(state)) break;
         state = _behavior.toggleState(state);
         if (state === first) break;
      }
      _state = state;
   }

   //To get the browser-dependent representation for 'transparent'
   _transparentBackGround = _insertDiv().css('background-color');

   return {
      hook: function (target, behavior) {
         let $target = (target instanceof jQuery) ? target : $(target);
         let _this = this;
         $target.on('mouseenter', function (ev) {//enter
            const $evTarget = $(this);
            behavior = _createBehavior(behavior, $evTarget);
            if (behavior.needShow($evTarget)) {
               ev.stopPropagation();
               _this.activate($evTarget, behavior);
            }
         }).on('mouseleave', function (ev) { //leave
            _clearActivationTimer();
            ev.stopPropagation();
            if (behavior.mode === 'tooltip') {
               //console.log('CLOSING tooltip');
               _hide();
            }
         }).on('mousedown', function (ev) { //Needed, since after page-load, an element doesnt get entered. Even if its under the mouse-ptr.
            _clearActivationTimer();
         });
      },
      hide: _hide,
      hideNow: _hideNow,
      clearActivationTimer: _clearActivationTimer,
      doesFit: _doesFit,
      setDefaultBehaviorProp: _setDefaultBehaviorProp,
      neededWidth: function (target) {
         const node = target[0];
         return Math.max(node.scrollWidth, node.offsetWidth);
      },

      createBehavior: _createBehavior,

      getTarget: function () {
         return _target;
      },

      getText: function () {
         return _content;
      },
      toggleState: _toggleState,
      getToggleState: function () {
         return _state;
      },
      setToggleState: function (state) {
         _state = state;
         _behavior.showState(state);
      },
      isVisible: function () {
         return !$overlay.hasClass(OVL_HIDDEN);
      },

      activate: _activate,
   };
}
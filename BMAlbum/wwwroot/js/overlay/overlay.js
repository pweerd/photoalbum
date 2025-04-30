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


   const _defBehavior = {
      mode:'overlay',
      debug: DBG_DEFAULT,
      maxHStrategy: '100%',
      maxWStrategy: 'default',
      propagateClick: true,
      closeOnClick: true,
      useTargetsBackground: false,
      initialState: 'html',
      states: ['html', 'text', 'json', 'xml'],
      copyFont: true,
      copyContent: function ($dst) {
         let tmp = this.$target.html();
         $dst.html(tmp);
         return tmp;
      },
      applyExtraStyles: function ($dst) {
      },
      toggleState: function _toggleState(state) {
         if (state === 'fixed') return state;
         states = this.states;
         ix = (states.indexOf(state) + 1) % states.length;
         return states[ix];
      },
      createPositionParms: function ($div) {
         let ovlOffsetY = _getBorderAndPaddingY($overlay);
         let offsetX = _getBorderAndPaddingX($overlay) - _getBorderAndPaddingX(this.$target);
         let offsetY = ovlOffsetY - _getBorderAndPaddingY(this.$target);
         if (!this.ignoreInnerPadding) {
            offsetX += _getBorderAndPaddingX($div);
            offsetY += _getBorderAndPaddingY($div);
         }
         return {
            my: "left top",
            at: "left-" + offsetX + "px top-" + offsetY + "px",
            of: $target,
            collision: "fit"
         };
      },
      needShow: function () {
         return !_doesFit(this.$target);
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
      }
   };
   const _tooltipBehavior = $.extend({}, _defBehavior, {
      mode: 'tooltip',
      initialState: 'fixed',
      createPositionParms: function ($div) {
         return {
            my: "left top",
            at: "left+2px bottom+2px",
            of: this.$target,
            collision: "fit"
         };
      },
      needShow: function () {
         return this.$target.attr('data-title');
      },
      copyContent: function ($dst) {
         this.showState('html');
         let title = this.$target.attr('data-title');
         $dst.text(title);
         return title;
      }


   });
   const _scrollBehavior = $.extend({}, _defBehavior, {
      mode: 'scroll',
      closeOnClick: true
   });

   function _setDefaultBehaviorProp(k, v) {
      _defBehavior[k] = v;
      _scrollBehavior[k] = v;
      _tooltipBehavior[k] = v;
   }

   function _createBehavior(overlay, behavior, $target) {
      let ret = behavior;
      if (!behavior || !behavior._isExtended) {
         let _def = _defBehavior;
         if (behavior !== null && typeof behavior === 'object' && !Array.isArray(behavior)) {
            switch (behavior.mode) {
               case 'scroll': _def = _scrollBehavior; break;
               case 'tooltip': _def = _tooltipBehavior; break;
            }
         }
         ret = $.extend({}, _def, behavior);
         ret._isExtended = true;
      }
      ret.$target = $target;
      ret.overlay = overlay;
      return ret;
   }

   let _behavior = _defBehavior;

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
      $overlay.html("<div></div>");
      return $overlay.find("div");
   }

   function _hideNow() {
      _clearActivationTimer();
      if (!DBG_NO_HIDE) {
         $overlay.addClass(OVL_HIDDEN);
         if (!_behavior.debug) $overlay.html('');
      }
      _behavior = _defBehavior;
      _content = undefined;
      _target = undefined;
   }

   function _clearActivationTimer() {
      if (_activationTimer) {
         clearTimeout(_activationTimer);
         _activationTimer = undefined;
      }
   }
   function _clearHideTimer() {
      if (_hideTimer) {
         clearTimeout(_hideTimer);
         _hideTimer = undefined;
      }
   }

   function _hide() {
      _clearActivationTimer();
      _hideTimer = setTimeout(function () { //Cant remember why this timer was needed. Maybe to cope with a hide(), immediately followed by an activate()
         _hideTimer = undefined;
         _hideNow();
      }, 20);
   }

   function _activateNow(behavior) {
      const dbg = behavior.debug;
      const $target = behavior.$target;

      _clearActivationTimer();
      _clearHideTimer();
      if (_target && _target[0] === $target[0]) {
         console.log("NOT activated: already active");
         return;
      }
      if (behavior.activationPos) {
         const rect = $target[0].getBoundingClientRect();
         if (rect.left !== behavior.activationPos.x || rect.top !== behavior.activationPos.y) {
            console.log("NOT activated: target moved");
            return;
         }
      }

      _target = $target;
      _behavior = behavior;
      $div = _insertDiv();
      const neededW = _this.neededWidth($target);
      const offset = $target.offset();
      let maxW = window.innerWidth / 2;
      let targetW = $target.width();// $target[0].offsetWidth;
      if (targetW > maxW) maxW = targetW;
      $overlay.css('max-width', maxW).css('min-width', targetW);
      _state = behavior.initialState;

      //$overlay.css('max-height', maxH + "px");
      if (behavior.mode === "scroll" || behavior.mode === "tooltip" || neededW + offset.left > maxW) {
         if (dbg) console.log("OVL: Need largeTooltip");
         $overlay.addClass(OVL_SCROLL);
         if (behavior.copyFont) _copyFont($div, $target);
         _content = behavior.copyContent($div);
         _state = _convertAutoState(_state);
         if (_content && !behavior.showState(_state)) behavior.showState(_state='text');
      } else {
         //Make sure that we are hidden when a wheel event occurs.
         //Reason is that the div below us should handle the event in case of a non-scrolling overlay
         $div[0].addEventListener("wheel", function (ev) {
            if (dbg) console.log("OVL: wheel");
            _hideNow();
         }, { passive: false });

         $overlay.removeClass(OVL_SCROLL);

         maxW = window.innerWidth-20;
         $div.height($target.innerHeight() + 3);
         if (!behavior.useTargetsBackground) {
            if (dbg) console.log("OVL: own background");
            if (behavior.copyFont) _copyFont($div, $target);
            behavior.copyContent($div);
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

      if ("string" === typeof behavior.applyExtraStyles)
         $div.addClass(behavior.applyExtraStyles);
      else
         behavior.applyExtraStyles($div);

      _reposition($div);
   }

   // Reposition the overlay in 2 steps:
   // - first position
   // - apply max-height based on the position
   // - position another time
   function _reposition($div) {
      //console.trace('reposition');
      if (!$div) $div = $overlay.find("div");
      const parms = _behavior.createPositionParms($div);
      //console.log("OVL: parms=", parms, ', tgt=', $target[0]);


      //Note: Not sure why: sometimes the 1st call positions the overlay a bit to the left...
      //However, since we modify the max-height, it makes sense to always do a 2nd call to position()
      $overlay.position(parms);

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
                  $overlay.css('max-width', '')
                  break;
               case 'default':
                  break;
               default:
                  $overlay.css('max-width', _behavior.maxWStrategy);
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
            let maxH, top;
            switch (_behavior.maxHStrategy) {
               case 'none': break;
               case 'target':
                  top = $target[0].getBoundingClientRect().top;
                  maxH = window.innerHeight - top;
                  break;
               case 'overlay':
                  top = parseFloat($overlay.css('top'));
                  maxH = window.innerHeight - top;
                  break;
               default:
                  maxH = _behavior.maxHStrategy;
                  break;
            }
            if (maxH) $overlay.css('max-height', maxH);
            break;
      }
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
      if (state === 'auto') {
         if (!_content) return "text";
         nJson = 0;
         nXml = 0;
         nOther = 2;
         N = _content.length;
         if (N > 1024) N = 1024;
         for (i = 0; i < N; i++) {
            switch (_content[i]) {
               case '[':
               case ']':
               case '{':
               case '}':
               case ':': ++nJson; continue;
               case '=':
               case '<':
               case '>': ++nXml; continue;
               case ';': nOther += 2; ++nXml; continue;
               case ',': nOther += 2; ++nJson; continue;
               case '\n': nOther += 2; ++nXml; ++nJson; continue;
            }
         }
         console.log('_convertAutoState: json,xml,other=', nJson, nXml, nOther);
         if (nJson > nXml+2 && nJson > nOther) return "json";
         if (nXml > nJson+2 && nXml > nOther) return "xml";
         return "text";
      }
      return state;
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
      let ix1 = -1;
      if (ixobj1 >= 0 && ixobj2 > ixobj1) {
         if (ixarr1 >= 0 && ixarr2 > ixarr1) {
            if (ixarr1 < ixobj1) {
               ix1 = ixarr1;
               ix2 = ixarr2;
            } else {
               ix1 = ixobj1;
               ix2 = ixobj2;
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

   $overlay.on ('mouseleave', function (ev) {
      if (_behavior.debug) console.log("OVL:mouseleave ", ev);
      ev.stopPropagation();
      if (ev.ctrlKey) return;
      _hide();
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
      _reposition();
   });


   function _propagate(ev, type) {
      console.log('OVL: propagate click, target=', _target, ev);
      if (!_target) return;

      function _triggerEvent(ev, tgt) {
         ev.target = tgt;
         ev.currentTarget = tgt;
         ev.delegateTarget = tgt;
         //console.log('propagating: ', ev, ' oldT=', o);
         $(tgt).trigger(ev);
      }

      let $myInputs = $overlay.children().find(ev.target.nodeName); //get the nodename under TTOverlay (the only child in #overlay)
      let $theirInputs = _target.find(ev.target.nodeName);
      if ($myInputs.length === $theirInputs.length) {
         console.log('OVL: lists are equal');
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

   function _doesFit (target, maxDepth) {
      let t = target;
      if (target instanceof jQuery) {
         if (target.length === 0) return true;
         t = target[0];
      }
      //console.log();
      //console.log('------------------------------------------------------------');
      let cnt = isNaN(maxDepth) ? 10 : maxDepth;
      while (t && --cnt > 0) {
         const name = t.nodeName.toUpperCase();
         if (name === '#DOCUMENT') break;
         //console.log(t.nodeName, t.id, 'sw', t.scrollWidth, 'ow', t.offsetWidth);
         if (t.scrollWidth > t.offsetWidth + 2) {
            //console.log('NOFIT', t.scrollWidth - t.offsetWidth);
            return false;
         }
         if (name === 'LI') break;
         if (name === 'UL') break;
         t = t.parentNode;
      }
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
            $evTarget = $(this);
            behavior = _createBehavior(_this, behavior, $evTarget);
            if (behavior.needShow()) {
               ev.stopPropagation();
               _this.activate($evTarget, behavior);
            }
         }).on('mouseleave', function (ev) { //leave
            _clearActivationTimer();
            ev.stopPropagation();
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

      activate: function (target, behavior, argDelay) {
         _this = this;
         _clearActivationTimer();
         const dbg = behavior && behavior.debug;

         $target = (target instanceof jQuery) ? target : $(target);
         if (dbg) console.log("Target=", typeof $target, $target);
         if ($target.length === 0) return;
         behavior = _createBehavior(this, behavior, $target);

         //Save activation position to use in the real activation function
         const rect = $target[0].getBoundingClientRect();
         behavior.activationPos = {
            x: rect.left,
            y: rect.top
         };


         let delay = argDelay;
         if (delay === undefined) delay = behavior.delay;
         if (delay < 20) delay = 20;

         if ($overlay.hasClass(OVL_HIDDEN)) {
            _activationTimer = setTimeout(function () { _activateNow(behavior); }, delay);
         } else {
            _activateNow(behavior);
         }
      }
   };
}
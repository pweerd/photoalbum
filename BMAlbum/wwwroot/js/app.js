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

function pinchAndZoom(target, txt) {
   let imageElementScale = 1;

   let start = {};

   // Define a flag to keep track of initial load
   let initialLoad = true;

   // Define variables to keep track of the existing transform values
   let translateX = 0;
   let translateY = 0;

   // Calculate distance between two fingers
   const distance = (event) => {
      return Math.hypot(event.touches[0].pageX - event.touches[1].pageX, event.touches[0].pageY - event.touches[1].pageY);
   };

   target.addEventListener('touchstart', (event) => {
      console.log('pinchAndZoom:start', txt, event.touches.length);
      if (event.touches.length === 2) {
         event.preventDefault(); // Prevent page scroll
         event.stopPropagation();
         //return;


         // Calculate where the fingers have started on the X and Y axis
         start.x = (event.touches[0].pageX + event.touches[1].pageX) / 2;
         start.y = (event.touches[0].pageY + event.touches[1].pageY) / 2;
         start.distance = distance(event);
      }
   });

   target.addEventListener('touchmove', (event) => {
      console.log('pinchAndZoom:move', txt, event.touches.length);
      if (event.touches.length === 2) {
         event.preventDefault(); // Prevent page scroll
         event.stopPropagation();
         //return;

         // Safari provides event.scale as two fingers move on the screen
         // For other browsers just calculate the scale manually
         let scale;
         if (event.scale) {
            scale = event.scale;
         } else {
            const deltaDistance = distance(event);
            scale = deltaDistance / start.distance;
         }
         imageElementScale = Math.min(Math.max(1, scale), 4);

         // Check if it's the initial load
         if (initialLoad) {
            // Get the existing transform style property for proper calculations
            var style = window.getComputedStyle(target);
            const existingTransform = style.getPropertyValue('transform');

            if (existingTransform.toString() !== "none") {
               const rect = target.getBoundingClientRect();
               translateX = -rect.width / 2;
               translateY = -rect.height / 2;
            }
            initialLoad = false; // Update the flag to indicate initial load has occurred
         }

         // Calculate how much the fingers have moved on the X and Y axis
         const deltaX = (((event.touches[0].pageX + event.touches[1].pageX) / 2) - start.x) * 2; // x2 for accelerated movement
         const deltaY = (((event.touches[0].pageY + event.touches[1].pageY) / 2) - start.y) * 2; // x2 for accelerated movement

         // Combine the existing transform with the additional calculations
         const transform = `translate3d(` + (translateX + deltaX) + `px, ` + (translateY + deltaY) + `px, 0) scale(` + imageElementScale + `)`;
         target.style.transform = transform;

         target.style.WebkitTransform = transform;
         //target.style.zIndex = "9999";
      }
   });

   target.addEventListener('touchend', (event) => {
      console.log('pinchAndZoom:end', txt, event.touches.length);
      if (event.touches.length === 2) {
         event.preventDefault(); // Prevent page scroll
         event.stopPropagation();
         //return;

         // Reset target to it's original format
         target.style.transform = "";
         target.style.WebkitTransform = "";
         target.style.zIndex = "";
         //reset initialLoad and translateX and translateY needed to apply the existing transform on image
         initialLoad = true;
         translateX = 0;
         translateY = 0;
      }
   });
}


const dbg_overlay = false;

function createApplication(state, fnInit) {
   const _clientLog = createClientLog(state.home_url + '_clientlog');
   _clientLog.hookError(false);
   if ((state.debug_flags & 0x10000) !== 0) {
      _clientLog.hookConsole();
   }
   hookHistory();

   const _histKeys = [
      'mode',
      'q',
      'pin',
      'per_album',
      'sort',
      'album',
      'year',
      'slide',
      'center',
      'zoom',
   ];
   function _copyStateParms(dst, src) {
      for (let i = 0; i < _histKeys.length; i++) {
         const k = _histKeys[i];
         dst[k] = src[k];
      };
   }


   Object.setPrototypeOf(state, {
      createUrl: function (relPath) {
         let parts = [this.user_home_url];
         if (relPath) {
            if (relPath.startsWith('/')) relPath = relPath.substring(1);
            parts.push(relPath);
            if (!relPath.endsWith('/')) parts.push('/');
         }
         let sep = '?';
         if (this.home_url_params) {
            parts.push(sep);
            sep = '&';
            parts.push(this.home_url_params);
         }
         for (let i = 0; i < _histKeys.length; i++) {
            const k = _histKeys[i];
            let v = state[k];
            if (typeof (v) === 'object') v = v.id;
            if (!v && v !== false) continue;
            parts.push(sep);
            sep = '&';
            parts.push(k + '=' + encodeURIComponent(v));
         };
         return parts.join('');
      },

      getJSON: function (relUrl, callback) {
         window._getJSON(this.createUrl(relUrl), callback);
      },

      postJSON: function (relUrl, payload, callback) {
         window._postJSON(this.createUrl(relUrl), payload, callback);
      },

      saveActiveState: function (newState) {
         if (newState) _copyStateParms(this, newState);
         this.activeState = {};
         _copyStateParms(this.activeState, this);
      },

      clear: function () {
         for (let i = 0; i < _histKeys.length; i++) {
            const k = _histKeys[i];
            this[k] = undefined;
         };
      },

      pushHistory: function (from, forceReplace) {
         let histState = { from: from };
         _copyStateParms(histState, this);
         histState.url = this.createUrl();

         let pushHist = history.pushState;
         if (!history.state || forceReplace)
            pushHist = history.replaceState;

         pushHist.call(history, histState, '', histState.url);
      },

      isChanged: function (otherState) {
         if (!otherState) otherState = this.activeState;
         if (!otherState) return true;
         for (let i = 0; i < _histKeys.length; i++) {
            const k = _histKeys[i];
            if (this[k] !== otherState[k]) return true;
         };
         return false;
      }
   });


   const _state = state;

   function _getEntryUrl() {
      let url = new URL(window.location);
      url.search = '';
      return url.href.endsWith('/') ? url.href : url.href + '/';
   }

   //Save the original url for using in the history
   const _entryUrl = _getEntryUrl();
   _state.entryUrl = _entryUrl;
   _state.cmd ||= '';

   _state.user_home_url = state.user ? _state.home_url + _state.user + '/' : _state.home_url;

   function _createUrl(relPath, parms) {
      console.log("CreateUrl", relPath, parms);
      let parts = [_state.user_home_url];
      if (relPath) {
         if (relPath.startsWith('/')) relPath = relPath.substring(1);
         parts.push(relPath);
         if (!relPath.endsWith('/')) parts.push('/');
      }
      parts.push('?');
      if (_state.home_url_params) parts.push(_state.home_url_params + "&");

      if (parms instanceof Array) {
         parts = parts.concat(parms);
      } else if (parms) {
         parts.push(parms);
      }

      let ret = parts.join("");
      console.log("-->", ret);
      return ret;
   }

   function _getJSON(relUrl, parms, callback, timeout) {
      return window._getJSON(_createUrl(relUrl, parms), callback, timeout);
   }
   function _postJSON(relUrl, payload, parms, callback, timeout) {
      return window._postJSON(_createUrl(relUrl, parms), payload, callback, timeout);
   }


   function _dumpHistory(why) {
      //console.log('Dumping history. length=', history.length, ", Why=", why, ', state=', history);
   }


   function _onPopHistory(ev) {
      if (!_app) return; //Not yet initialized
      const histState = history.state || {};
      _copyStateParms(_state, histState);
      console.log('HISTORY popped:', history.length, histState, _state);
      switch (_state.mode) {
         case "faces":
         case "photo":
         case "photos":
            if (!_app.lbControl) return; //Not yet initialized
            _state.center = undefined;
            _state.zoom = undefined;
            _app.lbControl.onPopHistory(ev);
            _enableOrDisableMap(false);
            break;
         case "map":
            if (!_app.mapControl) return; //Not yet initialized
            _app.mapControl.onPopHistory(ev);
            _enableOrDisableMap(true);
            break;
         case "facephoto":
            if (!_app.facePhotoControl) return; //Not yet initialized
            _app.facePhotoControl.onPopHistory(ev);
            break;
         default:
            console.log('INVALID mode: [', histState.mode, ']', histState);
            break;
      }
   }


   function _start(from, recursive) {
      //if (from==="history") _copyStateParms(_state, history.state);
      console.log("Start: from=", from, ", history=", history.state);
      _overlay.hideNow();

      switch (_state.mode) {
         case "faces":
         case "photo":
         case "photos":
            _state.center = undefined;
            _state.zoom = undefined;
            if (_app.lbControl.start(from, recursive)) _enableOrDisableMap(false);
            break;
         case "map":
            if (_app.mapControl.start(from, recursive)) _enableOrDisableMap(true);
            break;
         default:
            alert('invalid mode: [' + _state.mode + ']');
            break;
      }
      if (_state.mode == "faces") {
         $("html").css("overflow-y","hidden");
         $("#row_album").addClass("hidden");
         $("#row_year").addClass("hidden");
         $("#row_per_album").addClass("hidden");
      }
   }

   window.addEventListener('popstate', _onPopHistory)
   const _overlay = createOverlay('#overlay');
   _overlay.setDefaultBehaviorProp('maxWStrategy', '100%');
   _overlay.setDefaultBehaviorProp('maxHStrategy', '100%');
   _overlay.setDefaultBehaviorProp('debug', dbg_overlay);
   _overlay.setDefaultBehaviorProp('closeOnClick', true);
   _overlay.setDefaultBehaviorProp('propagateClick', false);

   let _savedScrollTop = -1;
   function _enableOrDisableMap(enable) {
      //We made the scroll directly appearing in the window (for IPhone: otherwise scroll issues)
      //So now we have to keep track of the scroll position when we swap from map-view to photo-view.
      //also the map needs the body to have a height
      let $map = $("#map");
      let $photos = $("#photos");
      let $lg = $(".lg-container");
      if (enable) {
         _savedScrollTop = document.documentElement.scrollTop || document.body.scrollTop;
         document.body.style.height = "100dvh";
         document.documentElement.style.overflowY = "hidden";
         _scrollTo(0);
         $lg.addClass("hidden");
         $photos.addClass("hidden");
         $map.removeClass("hidden");
      } else {
         document.documentElement.style.overflowY = "scroll";
         if (_savedScrollTop>=0) _scrollTo(_savedScrollTop);
         _savedScrollTop = -1;
         document.body.style.height = "";
         $map.addClass("hidden");
         $photos.removeClass("hidden");
         $lg.removeClass("hidden");
      }
   }

   function _scrollTo(y, behavior) {
      //console.trace("SCROLLTO", y, behavior);
      window.scrollTo({ top: y, behavior: (behavior ?? 'instant') });
   }


   let _sensors = undefined;
   let _app = {
      clientLog: _clientLog,
      dumpHistory: _dumpHistory,
      createUrl: _createUrl,
      getJSON: _getJSON,
      postJSON: _postJSON,
      state: _state,
      overlay: _overlay,
      start: _start,
      sensors: function () { if (!_sensors) _sensors = createSensorsApi(); return _sensors; },
      initDummySensors: function () { _sensors = createDummySensorsApi(); },
      scrollTo: _scrollTo,
   };

   function getDeviceType() {
      //NB these constants should match the BrowserType enum in LightboxSettings.cs
      const DESKTOP = 1;
      const PHONE = 2;
      const TABLET = 4;

      if (device.isPhone) return PHONE;
      if (device.isTablet) return TABLET;
      return DESKTOP;
   }
   _getJSON("home/config", "dvt=" + getDeviceType(), function (fetcher) {
      _app.config = fetcher.json;
      fnInit(_app)
   });
   return _app; //Experiment: remove this for pinchAndZoom

   if (device.isIPhone || device.isIPad) {
      document.addEventListener('gesturestart', function (e) {
         e.preventDefault();
      });
      console.log('pinchAndZoom');
      pinchAndZoom(document.body, 'body');
   }
   return _app;
}



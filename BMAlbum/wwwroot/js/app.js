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

const dbg_overlay = false;
//NB these constants should match the BrowserType enum in LightboxSettings.cs
const DESKTOP = 1;
const PHONE = 2;
const TABLET = 4;

function createApplication(state) {

   if ((state.debug_flags & 0x10000) !== 0 && hookConsole)
      hookConsole(state.home_url + '_clientlog');
   hookHistory();

   String.prototype.format = function () {
      var args = arguments;
      return this.replace(/{([0-9]+)}/g, function (match, index) {
         return typeof args[index] == 'undefined' ? match : args[index];
      });
   };

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
         _getOrPostJsonBackend(this.createUrl(relUrl), undefined, callback);
      },

      postJSON: function (relUrl, payload, callback) {
         _getOrPostJsonBackend(this.createUrl(relUrl), payload, callback);
      },

      saveActiveState: function (newState) {
         if (newState) _copyStateParms(this, newState);
         this.activeState = {};
         _copyStateParms(this.activeState, this);
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
   let _isTouch = false;

   function _getEntryUrl() {
      let url = new URL(window.location);
      url.search = '';
      return url.href.endsWith('/') ? url.href : url.href+'/';
   }

   //Save the original url for using in the history
   const _entryUrl = _getEntryUrl();
   _state.entryUrl = _entryUrl;
   _state.cmd ||= '';

   _state.user_home_url = state.user ? _state.home_url + _state.user + '/' : _state.home_url;

   let _device = function () {
      let ua = navigator.userAgent.toLowerCase();
      let mobile = /iphone|ipad|ipod|android/.test(ua);

      if (mobile) {
         return (/mobile/.test(ua)) ? PHONE : TABLET;
      }
      return DESKTOP;
   }();
   console.log("DEVICE", _device);

   function _createUrl(relPath, parms) {
      console.log("CreateUrl", relPath, parms);
      let parts = [_state.user_home_url];
      if (relPath) {
         if (relPath.startsWith('/')) relPath = relPath.substring(1);
         parts.push(relPath);
         if (!relPath.endsWith('/')) parts.push('/');
      }
      parts.push('?');
      if (this.home_url_params) parts.push(this.home_url_params);

      if (parms instanceof Array) {
         parts = parts.concat(parms);
      } else if (parms) {
         parts.push(parms);
      }

      let ret = parts.join("");
      console.log("-->", ret);
      return ret;
   }

   function _getOrPostJsonBackend(url, dataToSend, func) {
      let method="GET", payload;

      if (dataToSend) {
         payload = JSON.stringify(dataToSend);
         method = "POST"
      }
      $.ajax({
         type: method,
         dataType: "json",
         contentType: "application/json",
         data: payload,
         url: url,
         complete: function (jqXHR) {
            let json;
            let unknownMsg = 'Unknown error: ';
            try {
               json = JSON.parse(jqXHR.responseText);
            } catch (err) {
               unknownMsg = err + ': ';
            }
            if (jqXHR.status === 200 && json && json.bm_error === undefined) {
               func(json, jqXHR);
               return;
            }
            let arr = [];
            if (json && json.bm_error) {
               arr.push(json.bm_error.message);
            } else {
               arr.push(unknownMsg + jqXHR.responseText);
            }
            arr.push("\r\n\r\nUrl=");
            arr.push(url);
            if ((_state.debug_flags & 4) != 0 && json && json.bm_error.stacktrace) {
               arr.push("\r\nTrace=");
               arr.push(json.bm_error.stacktrace);
            }
            alert(arr.join(''));
         }
      });
   }
   function _getJSON(relUrl, parms, callback) {
      return _getOrPostJsonBackend(_createUrl(relUrl, parms), undefined, callback);
   }
   function _postJSON(relUrl, payload, parms, func) {
      return _getOrPostJsonBackend(_createUrl(relUrl, parms), payload, func);
   }


   function _detectTouch(ev) {
      _isTouch = true;
      $(window).off('touchstart', _detectTouch);
      console.log("touch event: ", ev.originalEvent.type);
   }



   function _dumpHistory(why) {
      //console.log('Dumping history. length=', history.length, ", Why=", why, ', state=', history);
   }

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


   function _onPopHistory(ev) {
      let histState = history.state || {};
      _copyStateParms(_state, histState);
      console.log('HISTORY popped:', history.length, histState, _state);
      switch (_state.mode) {
         case "faces":
         case "photo":
         case "photos":
            _state.center = undefined;
            _state.zoom = undefined;
            app.lbControl.onPopHistory(ev);
            _enableOrDisableMap(false);
            break;
         case "map":
            app.mapControl.onPopHistory(ev); 
            _enableOrDisableMap(true);
            break;
         default:
            console.log('INVALID mode: [', histState.mode, ']', histState);
            break;
      }
   }


   function _start(from) {
      //if (from==="history") _copyStateParms(_state, history.state);
      console.log("Start: from=", from, ", history=", history.state); 
      _overlay.hideNow();

      switch (_state.mode) {
         case "faces":
         case "photo":
         case "photos":
            _state.center = undefined;
            _state.zoom = undefined;
            if (app.lbControl.start(from)) _enableOrDisableMap(false);
            break;
         case "map":
            if (app.mapControl.start(from)) _enableOrDisableMap(true);
            break;
         default:
            alert('invalid mode: [' + _state.mode + ']');
            break;
      }
   }

   $(window).on('popstate', _onPopHistory)
   let _overlay = createOverlay('#overlay');
   function _enableOrDisableMap(enable) {
      let $map = $("#map");
      let $main = $("#main");
      let $lg = $(".lg-container");
      if (enable) {
         $lg.addClass("hidden");
         $main.addClass("hidden");
         $map.removeClass("hidden");
      } else {
         $map.addClass("hidden");
         $main.removeClass("hidden");
         $lg.removeClass("hidden");
      }
   }


   return {
      dumpHistory: _dumpHistory,
      createUrl: _createUrl,
      getJSON: _getJSON,
      postJSON: _postJSON,
      state: _state,
      device: _device,
      isTouch: _isTouch,
      overlay: _overlay,
      start: _start
   };
}


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


   let _state = state;
   let _isTouch = false;
   let _zoomer; 
   let _multipleAlbums = false;
   let _unique = 0;
   let _faceNames = { chgid: -1 };
   let _lightboxSizes = state.lightbox_settings;

   //Save the original url for using in the history
   _state.entryUrl = new URL(window.location);
   _state.entryUrl.search = ''
   _state.entryUrl = _state.entryUrl.href;
   _state.cmd ||= '';

   //Register keydown as first thing: we need to be able to cancel processing from lg...
   $(window)
      .on('keydown', function (ev) {
         if (_zoomer) return _zoomer.onKeyDown(ev);
      })
      ;

   let _device = function () {
      let ua = navigator.userAgent.toLowerCase();
      let mobile = /iphone|ipad|ipod|android/.test(ua);

      if (mobile) {
         return (/mobile/.test(ua)) ? PHONE : TABLET;
      }
      return DESKTOP;
   }();
   console.log("DEVICE", _device);

   function _normalizeCmd(str) {
      if (!str) return str;
      let arr = str.split('&');
      let values = {};
      for (let i = 0; i < arr.length; i++) {
         let v = arr[i];
         let j = v.indexOf('=');
         if (j < 0) {
            delete values[v];
            continue;
         }
         values[v.substring(0, j)] = v;
      }
      return Object.values(values).join('&');
   }
   function _createUrlParts(url) {
      let u = _state.user ? _state.user + "/" : "";
      return [_state.home_url, u, url, "?", _state.home_url_params];
   }

   function _createUrl(url, parms) {
      console.log("CreateUrl", url, parms);
      var parts = _createUrlParts(url);
      if (parms instanceof Array) {
         parts = parts.concat(parms);
      } else if (parms) {
         parts.push('&');
         parts.push(parms);
      }
      console.log("-->", parts.join(""));
      return parts.join("");
   }

   function _getOrPostJSON(url, dataToSend, parms, func) {
      let realUrl = _createUrl(url, parms);
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
         url: realUrl,
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
            arr.push(realUrl);
            if ((_state.debug_flags & 4) != 0 && json && json.bm_error.stacktrace) {
               arr.push("\r\nTrace=");
               arr.push(json.bm_error.stacktrace);
            }
            alert(arr.join(''));
         }
      });
   }
   function _getJSON(url, parms, func) {
      return _getOrPostJSON(url, undefined, parms, func);
   }


   function _detectTouch(ev) {
      _isTouch = true;
      $(window).off('touchstart', _detectTouch);
      console.log("touch event: ", ev.originalEvent.type);
   }



   function _dumpHistory(why) {
      //console.log('Dumping history. length=', history.length, ", Why=", why, ', state=', history);
   }


   function _onPopHistory(ev) {
      let histState = history.state || {};
      console.log('HISTORY popped:', history.length, histState);
      switch (histState.mode || '') {
         case "faces":
         case "photos":
            _state.mode = histState.mode;
            _state.cmd = _normalizeCmd(histState.cmd + "&mode=" + histState.mode);
            app.lbControl.onPopHistory(ev);
            _enableOrDisableMap(false);
            break;
         case "map":
            _state.mode = histState.mode;
            _state.cmd = _normalizeCmd(histState.cmd + "&mode=" + histState.mode);
            app.mapControl.onPopHistory(ev); 
            _enableOrDisableMap(true);
            break;
         default:
            console.log('INVALID mode: [', histState.mode, ']', histState);
            break;
      }
   }


   function _start(parms, from) {
      console.log("Start parms=", parms, ", from=", from, ", history=", history.state); 
      _overlay.hideNow();

      if (parms && parms.mode) _state.mode = parms.mode;
      switch (_state.mode) {
         case "faces":
         case "photos":
            _state.cmd = _normalizeCmd(state.cmd + "&mode=" + _state.mode);
            if (app.lbControl.start(parms, from)) _enableOrDisableMap(false);
            break;
         case "map":
            _state.cmd = _normalizeCmd(state.cmd + "&mode=" + _state.mode);
            if (app.mapControl.start(parms,from)) _enableOrDisableMap(true);
            break;
         default:
            alert('invalid mode: [' + mode + ']');
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
      normalizeCmd: _normalizeCmd,
      createUrl: _createUrl,
      getJSON: _getJSON,
      postJSON: _getOrPostJSON,
      state: _state,
      device: _device,
      isTouch: _isTouch,
      overlay: _overlay,
      start: _start
   };
}


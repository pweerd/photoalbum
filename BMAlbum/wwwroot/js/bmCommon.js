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

String.prototype.format = function () {
   var args = arguments;
   return this.replace(/{([0-9]+)}/g, function (match, index) {
      return typeof args[index] == 'undefined' ? match : args[index];
   });
};

/**
 * ClientLog stuff
 */
function createClientLog(url, cap, timeout) {
   const _cap = cap ?? 100;
   const _timeout = timeout ?? 5000;
   const _url = url;
   let _cache = [];
   let _timer;

   function _sendCacheToServer() {
      if (_cache.length > 0) {
         _postJSON(_url, { msgs: _cache }, undefined, _timeout);
         _cache = [];
      }
   }

   function _addToCache(payload, flush) {
      payload.d = 0 + Date.now();
      _cache.push(payload);
      if (_timer) clearTimeout(_timer);
      if (_timeout === 0 || _cache.length > _cap || flush)
         _sendCacheToServer();
      else
         _timer = setTimeout(_sendCacheToServer, _timeout);
   }

   function _logToServer(type, args) {
      if (type === 'log' || type === 'trace') type = 'debug';
      let payload = [], str;
      if (arguments) {
         for (let i = 0; i < args.length; i++) {
            if (i > 0) payload.push(' ');

            let v = args[i];
            try {
               switch (typeof v) {
                  case "object": str = JSON.stringify(v); break;
                  case "undefined": str = "undefined"; break;
                  default: str = v.toString(); break;
               }
            } catch (err) {
               str = err;
            }
            payload.push(str);
         }
      }
      _addToCache({ t: type, m: payload.join('') });
   }
   function _logErrorToServer(obj) {
      const arr = ["Runtime error: "];
      let err = obj;
      if (!Error.isError(err) && Error.isError(err.error)) err = err.error;
      arr.push(err.message);
      if (err.filename) arr.push("\n-- File=" + err.filename);
      if (err.lineno) arr.push("\n-- Line=" + err.lineno);
      if (err.colno) arr.push("\n-- Col=" + err.colno);
      if (err.stack) {
         arr.push("\n-- Stack=");
         arr.push(err.stack);
      }
      console.error(err);
      _addToCache({ t: 'error', m: arr.join('') });
   }
   function _hookError() {
      window.addEventListener("error", _logErrorToServer);
   }

   function _hookConsole(types) {
      if (!Array.isArray(types)) {
         if (typeof types === 'string') types = [types];
         else types = ['log', 'info', 'error', 'warn', 'trace', 'debug'];
      }
      for (let i = 0; i < types.length; i++) {
         let type = types[i];
         let oldLog = console[type];
         let recurseDepth = 0;
         console[type] = function () {
            if (oldLog) oldLog.apply(console, arguments);
            if (recurseDepth === 0) {
               ++recurseDepth;
               try {
                  _logToServer(type, arguments);
               }
               catch (err) { }
               --recurseDepth;
            }
         };
         console.log('console.', type, ' is hooked.');
      }
   }


   return {
      hookError: _hookError,
      logErrorToServer: _logErrorToServer,
      logToServer: _logToServer,
      hookConsole: _hookConsole
   }
}

/**
 * History stuff
 */
function hookHistory() {
   let _oldPush;
   let _oldReplace;
   let _oldGo;
   let _oldBack;
   let _oldForward;

   function _pushState(state, title, url) {
      console.log('HISTORY old:', history.length, history.state);
      _oldPush.apply(history, arguments);
      console.log('HISTORY push:', history.length, history.state);
   }
   function _replaceState(state, title, url) {
      console.log('HISTORY old:', history.length, history.state);
      _oldReplace.apply(history, arguments);
      console.log('HISTORY replace:', history.length, history.state);
   }
   function _back() {
      console.log('HISTORY back');
      _oldBack.apply(history, arguments);
   }
   function _forward() {
      console.log('HISTORY forward');
      _oldForward.apply(history, arguments);
   }
   function _go(where) {
      console.log('HISTORY go: ', where);
      _oldGo.apply(history, arguments);
   }

   if (_oldReplace) {
      alert('History is already hooked');
   } else {
      let proto = Object.getPrototypeOf(history);
      _oldBack = proto['back'];
      proto['back'] = _back;
      _oldForward = proto['forward'];
      proto['forward'] = _forward;
      _oldGo = proto['go'];
      proto['go'] = _go;
      _oldPush = proto['pushState'];
      proto['pushState'] = _pushState;
      _oldReplace = proto['replaceState'];
      proto['replaceState'] = _replaceState;
      console.log("HISTORY hooked:", history);
   }
}

/**
 * Ajax stuff
 */
function _getOrPostJsonBackend(req, cb) {
   if (!cb) cb = function () { };

   function _setTimeout(ms) {
      if (fetcher._timer) clearTimeout(fetcher._timer);
      fetcher._timer = setTimeout(function () {
         fetcher.abort(new Error('timeout ' + ms + 'ms'));
      }, ms);
   }

   const fetcher = {
      controller: new AbortController(),
      abort: function (reason) {
         this.controller.abort(reason);
      },
      isAborted: function () {
         return this.controller.signal.aborted;
      },
      setTimeout: _setTimeout,
      errorHandler: _getOrPostJsonBackend.defaultErrorHandler,
   }

   fetcher.promise = fetch(req, { signal: fetcher.controller.signal })
      .then(rsp => {
         fetcher.response = rsp;
         return rsp.json();
      })
      .then(json => {
         fetcher.json = json;
         if (!fetcher.response.ok) {
            let msg = undefined;
            if (json && json.bm_error) {
               msg = json.bm_error.message;
            } else {
               msg = 'Statuscode ' + resp.status;
            }
            throw new Error(msg);
         }
         cb(fetcher);
         return Promise.resolve(fetcher);
      })
      .catch(err => {
         if (!Error.isError(err)) {
            let msg = err;
            if (fetcher.isAborted()) msg = 'Aborted: ' + JSON.stringify(msg);
            err = new Error(msg)
         }
         err.message = err.message + '\r\nUrl=' + req.url;
         fetcher.error = err;
         if (cb.length > 1) cb(fetcher, err); else fetcher.errorHandler(err);
         return Promise.resolve(fetcher);
      })
      .finally(() => {
         if (fetcher._timer) {
            clearTimeout(fetcher._timer); fetcher._timer = undefined;
         }
      });
   fetcher.setTimeout((req.timeout && req.timeout > 0) ? req.timeout : 2000);
   return fetcher;
}
_getOrPostJsonBackend.defaultErrorHandler = function(err) {
   dispatchError(err);
   alert(err.message);
};


function _getJSON(url, cb, timeout) {
   const _url = url;
   const _req = new Request(_url, {
      headers: {
         'Accept': 'application/json'
      }
   });
   _req.timeout = timeout;
   return _getOrPostJsonBackend(_req, cb);
}
function _postJSON(url, value, cb, timeout) {
   const _url = url;
   const _req = new Request(_url, {
      headers: {
         'Accept': 'application/json',
         'Content-Type': 'application/json'
      },
      method: "POST",
      body: typeof (value) === 'string' ? value : JSON.stringify(value)
   });
   _req.timeout = timeout;
   return _getOrPostJsonBackend(_req, cb);
}

function dispatchError(err) {
   const ev = new ErrorEvent('error', {
      message: err.message,
      filename: err.filename,
      lineno: err.lineno,
      colno: err.colno,
      error: err,
   });
   return dispatchEvent(ev);
}



/**
 * DeviceCharacteristics
 */
function _createDeviceCharacteristics() {
   function _dump(d) {
      console.log("device characteristics:", d);
      return d;
   }
   let isTouch = ('ontouchstart' in window) || (navigator.maxTouchPoints > 0) || (navigator.msMaxTouchPoints > 0);
   let ua = navigator.userAgent.toLowerCase();
   let isIOS = (/iphone|ipad|ipod/.test(ua));
   let isAndroid = (/android/.test(ua));
   let isDesktop = !isIOS && !isAndroid;
   let isPhone = false;
   let isTablet = false;
   if (!isDesktop) {
      if ((/mobile/.test(ua))) isPhone = true; else isTablet = true;
   }
   return _dump({
      isTouch: isTouch,
      isIOS: isIOS,
      isAndroid: isAndroid,
      isDesktop: isDesktop,
      isPhone: isPhone,
      isIPhone: isPhone && isIOS,
      isTablet: isTablet,
      isIPad: isTablet && isIOS,
   });
}
window.device = _createDeviceCharacteristics();

window.encode = function () {
   const entityMap  = {
      '&': '&amp;',
      '<': '&lt;',
      '>': '&gt;',
      '"': '&quot;'
   };
   const regexp = new RegExp('[&<>"]', 'g');

   return {
      html: function (s) {
         return s.replace(regexp, ch => entityMap[ch]);
      },
      url: encodeURIComponent,
   }
}();


// see if it looks and smells like an iterable object
function isArrayLike(item) {
   if (Array.isArray(item)) return true;
   if (!!item && typeof (item) === "object") {
      const len = item.length;
      if (!typeof (len) === "number") return false;
      if (len === 0) return true;
      return len > 0 && ((len - 1) in item);
   }
   return false;
}


//Hook _on() and _off()
(function () {
   function _on() {
      if (this.addEventListener) {
         this.addEventListener.apply(this, arguments);
         return this;
      }
      if (!isArrayLike(this)) {
         throw new Error("_on() cannot be used on " + this.toString());
      }
      for (let i = 0; i < this.length; i++) {
         const item = this[i];
         item.addEventListener.apply(item, arguments);
      }
      return this;
   }
   function _off() {
      if (this.removeEventListener) {
         this.removeEventListener.apply(this, arguments);
         return this;
      }
      if (!isArrayLike(this)) {
         throw new Error("_off() cannot be used on " + this.toString());
      }
      for (let i = 0; i < this.length; i++) {
         const item = this[i];
         item.removeEventListener.apply(item, arguments);
      }
      return this;
   }
   HTMLElement.prototype._on = _on;
   HTMLElement.prototype._off = _off;
   HTMLDocument.prototype._on = _on;
   HTMLDocument.prototype._off = _off;
   NodeList.prototype._on = _on;
   NodeList.prototype._off = _off;
   // Array.prototype._on = _on;
   // Array.prototype._off = _off;
   if ($ !== undefined) {
      $.prototype._on = _on;
      $.prototype._off = _off;
   }
}) ();

// function addLongPressHandler(elt, onClick, onLongClick, ms) {
//    let _touchTimer;
//    let _invokedHandler;
//    let _touchEvent;

//    if (elt instanceof jQuery) elt = elt[0];


//    function _onTouchStart(ev) {
//       console.log('LONGPRESS touch start');
//       _onTouchCancel();
//       if (ev.targetTouches.length > 1) return; //more fingers? Not for us

//       //Cannot stop propagation: we would interfere with other handlers then!
//       _touchEvent = ev;
//       _touchTimer = setTimeout(function () {
//          _invokedHandler = true;
//          onLongClick.call(elt, ev);
//       }, ms ?? 400);
//    }

//    function _onTouchEnd(ev) {
//       console.log('LONGPRESS touch end, _invokedHandler=', _invokedHandler);
//       if (_invokedHandler) {
//          ev.preventDefault();
//          ev.stopImmediatePropagation();
//       } else if (onClick !== undefined) onClick.call(elt, ev);
//       _onTouchCancel();
//    }

//    function _onTouchCancel() {
//       if (_touchTimer) {
//          clearTimeout(_touchTimer);
//          _touchTimer = undefined;
//       }
//       _invokedHandler = false;
//       _touchEvent = undefined;
//    }

//    function _destroy() {
//       elt.removeEventListener("touchstart", _onTouchStart);
//       elt.removeEventListener("touchend", _onTouchEnd);
//       elt.removeEventListener("touchcancel", _onTouchCancel);
//       elt.removeEventListener("touchmove", _onTouchCancel);
//    }

//    elt.addEventListener("touchstart", _onTouchStart);
//    elt.addEventListener("touchend", _onTouchEnd);
//    elt.addEventListener("touchcancel", _onTouchCancel);
//    elt.addEventListener("touchmove", _onTouchCancel);

//    return {
//       destroy: _destroy,
//    }
//}


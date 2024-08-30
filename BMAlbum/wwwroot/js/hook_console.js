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

(function () {
   let _cap = 100;
   let _timeout = 5000;
   let _cache = [];
   let _timer;
   let _url;

   function _sendCacheToServer() {
      if (_cache.length > 0) {
         $.ajax({
            type: "POST",
            contentType: "application/json; charset=utf-8",
            dataType: "json",
            url: _url,
            data: JSON.stringify({ msgs: _cache })
         });
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

   bmGlobals.logServerMsg = function (msg, type, flush) {
      _addToCache({
         t: type ? type : 'debug',
         m: msg ? msg.toString() : ''
      }, flush);
   };

   bmGlobals.setConsoleCache = function (cap, timeout) {
      _cap = cap;
      _timeout = (timeout !== undefined && timeout >= 0) ? timeout : 15000;
   };

   bmGlobals.hookConsole = function _hookConsole(url, types, cap, timeout) {
      _url = url;

      if (!Array.isArray(types)) {
         if (typeof types === 'string') types = [types];
         else types = ['log', 'info', 'error', 'warn', 'trace'];
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

})();
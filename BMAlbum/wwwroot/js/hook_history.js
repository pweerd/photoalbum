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
   let _oldPush;
   let _oldReplace;
   let _oldGo;
   let _oldBack;
   let _oldForward;

   function _pushState(state, title, url) {
      console.log('HISTORY push:', state, title, url);
      _oldPush.apply(history, arguments);
   }
   function _replaceState(state, title, url) {
      console.log('HISTORY replace:', state, title, url);
      _oldReplace.apply(history, arguments);
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
})();
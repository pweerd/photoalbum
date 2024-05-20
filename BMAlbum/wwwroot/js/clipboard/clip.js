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

//f() is a text-producing function.
//If not supplied, execEvent() will fetch the data from the target element
function createClipboard(f) {

   function _createHelper() {
      let ret = document.getElementById("cb-helper");
      if (ret) return ret;
      ret = document.createElement('textarea');
      ret.id = 'cb-helper';
      ret.style.position = 'absolute';
      ret.style.left = '-1000px';
      ret.style.top = document.body.scrollTop + 'px';
      document.body.appendChild(ret); 
      return ret;
   }
   const _func = f;
   const _ta = _createHelper();
   const _isSupported = document.queryCommandSupported && document.queryCommandSupported("copy");

   function _exec(cmd, txt) {
      _ta.value = txt;
      const oldFocus = document.activeElement;
      try {
         console.log('cb: txt len=', txt.length, _ta.value.length);
         window.getSelection().removeAllRanges();
         _ta.select();
         //console.log('cb selection', _ta.selectionStart, _ta.selectionEnd);
         let result = document.execCommand(cmd);
         //console.log('copy result', result);

         if (result === 'unsuccessful' || result === false) {
            alert ("Couldn't copy the result. Probaly too large...");
         }    
      } finally {
         window.getSelection().removeAllRanges();
         _ta.value = '';
         _ta.blur();
         if (oldFocus)
            oldFocus.focus();
      }
   }

   function _execEvent(cmd, e) {
      if (_func) {
         console.log("function", _func);
         _exec(cmd, _func());
         return;
      }
      let inp = $(e.target).data('cbtarget');
      if (!inp) inp = $(e.currentTarget).data('cbtarget');
      console.log("cb-input", inp);
      if (!inp) return;
      inp = $(inp);
      if (inp && inp.length > 0) {
         const txt = inp.text();
         console.log("cb-input text", txt);
         _exec(cmd, txt);
      }
   }

   return {
      copy: function (txt) {
         _exec('copy', txt);
      }, 
      oncopy: function (e) {
         _execEvent('copy', e);
      },
      isSupported: function () {
         return _isSupported;
      }

   }
};
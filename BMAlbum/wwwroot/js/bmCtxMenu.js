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
function createContextMenu($element, mainSelector, $menu, onClick, onMenuItem) {
   let _onMenu = onMenuItem;
   let _onMenuItemClick = onClick;
   let _this;

   function _hideMenu() {
      _this.context = undefined;
      $menu.removeClass("bm_menu_active");
      _onTouchCancel();
      if (_touchInvokedCtxMenu) {
         _clickTimer = setTimeout(function () {
            console.log("clear touchInvoked. Was", _touchInvokedCtxMenu);
            _touchInvokedCtxMenu = false;
            _clickTimer = undefined;
         }, 200);
      }
   }

   let _touchTimer;
   let _clickTimer;
   let _touchInvokedCtxMenu;
   let _touchTarget;
   function _onTouchStart(ev) {
      console.log('TOUCH start');
      _onTouchCancel();
      if (ev.targetTouches.length > 1) return; //more fingers? Not for us

      //Cannot stop propagation: we would interfere with other handlers then!
      _touchTarget = ev.target;
      if (_clickTimer) {
         clearTimeout(_clickTimer);
         _clickTimer = undefined;
      }
      _touchTimer = setTimeout(function () {
         _touchInvokedCtxMenu = true;
         _onContextMenu(ev);
      }, 400);
   }
   function _onOtherTouchStart(ev) {
      if (_touchInvokedCtxMenu) return; //otherwise we immediately close the menu
      console.log('TOUCH other, same=', ev.target === _touchTarget);
      if (ev.target === _touchTarget) return; //This is our own touch

      let $p = $(ev.target);
      console.log('target:', $p[0].id, $p.html());
      while ($p.length > 0) {
         if ($p[0] === $element[0]) { console.log('elt found'); return; }
         if ($p[0] === $menu[0]) { console.log('menu found'); return; }
         $p = $p.parent();
      }
      console.log('HIDE menu');
      _hideMenu();
   }


   function _onTouchEnd(ev) {
      console.log('TOUCH end, _touchInvokedCtxMenu=', _touchInvokedCtxMenu, ev.target.localName, ev.target.id);
      if (_touchInvokedCtxMenu) {
         ev.preventDefault();
         ev.stopImmediatePropagation();
      }
      _onTouchCancel();
   }
   function _onTouchCancel() {
      if (_touchTimer) {
         clearTimeout(_touchTimer);
         _touchTimer = undefined;
      }
      _touchTarget = undefined;
   }

   function _showMenu(ev, $target, $positionTarget, posExpr) {
      if (!$target) return undefined;
      let ix = $target.attr('data_ix');
      if (!ix) ix = $target.index(mainSelector);

      _this.context = {
         $target: $target,
         targetIndex : ix,
         $positionTarget: $positionTarget,
      }

      if (_onMenu) {
         if (false === _onMenu.call(_this, ev, _this.context)) {
            console.log('Ctxmenu cancelled by callback', ev);
            return;
         }
      }
      $menu.addClass('bm_menu_active').position({
         my: posExpr || "left+5px top", //left-30px top-30px",
         of: _this.context.$positionTarget,
         collision: "fit"
      });
   }

   function _onContextMenu(ev) {
      const $evTarget = $(ev.target);
      console.log('handle context menu', ev);
      _$target = mainSelector ? $evTarget.closest(mainSelector) : $evTarget;
      if (_$target.length === 0) return;

      ev.preventDefault();
      ev.stopImmediatePropagation();
      _showMenu(ev, _$target, ev);
   }

   function _onMenuClick(ev) {
      if (ev.button !== 0) return;
      ev.preventDefault();
      ev.stopImmediatePropagation();
      console.log("CTX: click on menu or item");
      let $item = $(ev.target).closest('.bm_menu_item');
      if ($item.length === 0 || $item.hasClass('bm_menu_disabled')) {
         console.log("menu skipped: disabled or not found");
         _hideMenu();
         return;
      }

      try {
         let ctx = _this.context;
         ctx.$clickedItem = $item;
         ctx.clickedId = $item[0].id;
         ctx.clickedIndex = $item.index();
         _onMenuItemClick.call (this, ev, ctx);
      } finally {
         _hideMenu();
      }
   }

   function _onMenuLeave(ev) {
      if (ev.pointerType === "touch") return;
      //console.log("ONMENULEAVE", ev.target.localName, ev.relatedTarget?.localName, ev.pointerType, typeof(_this));
      //console.log("ONMENULEAVE", new Error().stack);
      _hideMenu();
   }

   $element._on("contextmenu", _onContextMenu);
   $menu
      ._on("pointerleave", _onMenuLeave)
      ._on("click", _onMenuClick);
   if (device.isIPhone || device.isIPad) {
      $element
         ._on("touchstart", _onTouchStart)
         ._on("touchend", _onTouchEnd)
         ._on("touchcancel", _onTouchCancel)
         ._on("touchmove", _onTouchCancel);
      //window.addEventListener("touchstart", _onOtherTouchStart);
   }

   function _destroy() {
      $element._off("contextmenu", _onContextMenu);
      $menu
         ._off("pointerleave", _onMenuLeave)
         ._off("click", _onMenuClick);
      if (device.isIPhone || device.isIPad) {
         $element
            ._off("touchstart", _onTouchStart)
            ._off("touchend", _onTouchEnd)
            ._off("touchcancel", _onTouchCancel)
            ._off("touchmove", _onTouchCancel);
         //window.removeEventListener("touchstart", _onOtherTouchStart);
      }
   }

   function _enableMenuItem(selector, enabled) {
      let $item = $menu.find(selector);
      if ($item.length === 0) return;
      if (enabled) $item.removeClass('bm_menu_item_disabled');
      else $item.addClass('bm_menu_item_disabled')
   }
   function _showMenuItem(selector, shown) {
      let $item = $menu.find(selector);
      if ($item.length === 0) return;
      if (shown) $item.removeClass('bm_menu_item_hidden');
      else $item.addClass('bm_menu_item_hidden')
   }

   return _this = {
      destroy: _destroy,
      onMenu: function (fn) { _onMenu = fn; return this; },
      onMenuClick: function (fn) { _onMenuClick = fn; return this; },
      showMenuItem: _showMenuItem,
      enableMenuItem: _enableMenuItem,
      showMenu: _showMenu,
      close: _hideMenu,
      context: undefined,
      isMenuShown: function () {
         return _touchInvokedCtxMenu;
      },
   };
};
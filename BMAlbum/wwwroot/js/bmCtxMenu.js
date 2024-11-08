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
   }

   let _touchTimer;
   let _touchInvokedCtxMenu;
   let _touchTarget;
   function _onTouchStart(ev) {
      console.log('TOUCH start');
      _onTouchCancel();
      if (ev.targetTouches.length > 1) return; //more fingers? Not for us

      //Cannot stop propagation: we would interfere with other handlers then!
      _touchTarget = ev.target;
      _touchTimer = setTimeout(function () {
         _touchInvokedCtxMenu = true;
         _onContextMenu(ev);
      }, 400);
   }
   function _onOtherTouchStart(ev) {
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
      console.log('TOUCH end, _touchInvokedCtxMenu=', _touchInvokedCtxMenu);
      if (_touchInvokedCtxMenu) {
         ev.preventDefault();
         ev.stopImmediatePropagation();
      }
      _onTouchCancel();
   }
   function _onTouchCancel() {
      if (_touchTimer) clearTimeout(_touchTimer);
      _touchInvokedCtxMenu = false;
      _touchTarget = undefined;
   }

   function _showMenu(ev, $target, $positionTarget, posExpr) {
      if (!$target) return undefined;
      let ix = $target.attr('data_ix');
      if (!ix) ix = $target.index();

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
         _onMenuLeave(ev);
         return;
      }

      try {
         let ctx = _this.context;
         ctx.$clickedItem = $item;
         ctx.clickedId = $item[0].id;
         ctx.clickedIndex = $item.index();
         _onMenuItemClick.call (this, ev, ctx);
      } finally {
         _onMenuLeave(ev);
      }
   }

   function _onMenuLeave(ev) {
      _hideMenu();
   }


   $element.on("contextmenu", _onContextMenu);
   if (/iphone|ipad/.test(navigator.userAgent.toLowerCase())) {
      $element
         .on("touchstart", _onTouchStart)
         .on("touchend", _onTouchEnd)
         .on("touchcancel", _onTouchCancel)
         .on("touchmove", _onTouchCancel);
      $(window).on("touchstart", _onOtherTouchStart);
   }
   $menu
      .on("mouseleave", _onMenuLeave)
      .on("click", _onMenuClick);

   function _destroy() {
      $element.off("contextmenu", _onContextMenu);
      $menu
         .off("mouseleave", _onMenuLeave)
         .off("click", _onMenuClick);
      if (/iphone|ipad/.test(navigator.userAgent.toLowerCase())) {
         $element
            .off("touchstart", _onTouchStart)
            .off("touchend", _onTouchEnd)
            .off("touchcancel", _onTouchCancel)
            .off("touchmove", _onTouchCancel);
         $(window).off("touchstart", _onOtherTouchStart);
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
   };
};
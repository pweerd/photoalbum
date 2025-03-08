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


function createLightboxControl(app) {
   class LightboxState {
      imgHeight;
      ratio;
      sizeSettings;

      constructor(imgHeight, sizeSettings, ratio) {
         this.imgHeight = imgHeight;
         this.sizeSettings = sizeSettings;
         this.ratio = ratio;
      }

      isChanged(oldState) {
         return this.imgHeight != oldState.imgHeight || this.sizeSettings != oldState.sizeSettings;
      };

      limitRatio(ratio) {
         if (_device & this.sizeSettings.square_on) return 1;

         if (ratio < this.sizeSettings.ratio_lo) return this.sizeSettings.ratio_lo;
         if (ratio > this.sizeSettings.ratio_hi) return this.sizeSettings.ratio_hi;
         return ratio;
      }
   }

   const _device = app.device;
   const _state = app.state;
   let _faceMode;
   let _data;
   let _lg;
   let _lastLbState = new LightboxState(-1, null, 0);  //Last state of the lightbox
   let _isTouch = false;
   let _zoomer; 
   let _multipleAlbums = false;
   let _unique = 0;
   let _faceNames = { chgid: -1 };
   let _t0;

   function _prepareTiming() {
      _t0 = Date.now();
   }
   function _addTiming(data, action) {
      let dbg = data.dbg;
      if (!dbg) return;
      if (dbg.timings===undefined) dbg.timings = [];
      let t1 = Date.now();
      dbg.timings.push({
         took: t1 - _t0,
         action: action,
         count: data.files.length
      });
      _t0 = t1;
   }

   //Register keydown as first thing: we need to be able to cancel processing from lg...
   $(window).on('keydown', function (ev) {
      if (_zoomer) return _zoomer.onKeyDown(ev);
   });

   function _resetAll() {
      _overlay.hideNow();
      _ctxMenu.close();
      _zoomer.reset();
   }

   function _createImgMarkup(sb, photo, lbState) {
      sb.push('<a href="" class="lb-item" data-src="'); 
      sb.push(photo.imgUrl);
      sb.push('" thumb="');
      sb.push(photo.imgUrl + '&h=76"');
      sb.push(' data-lg-size="');
      sb.push(photo.w);
      sb.push('-');
      sb.push(photo.h);
      sb.push('"><div class="lb-wrapper"><img class="lb-image lazyload" src="data:image/gif;base64,R0lGODdhAQABAPAAAMPDwwAAACwAAAAAAQABAAACAkQBADs=" data-src="');
      sb.push(photo.imgUrl);

      let aspect = lbState.limitRatio(photo.w / photo.h);
      let h = lbState.imgHeight;
      let w = Math.round(h * aspect);
      sb.push('&h=240" width="' + w + '" height="' + h);
      sb.push('"><div class="txtbadge info-badge bottom-right-4"></div></div></a>');
   }

   function _createFaceMarkup(sb, photo, lbState) {
      function toDateString(dateStr) {
         if (!dateStr) return "";
         function pad(n) { return n < 10 ? '0' + n : n }
         let d = new Date(dateStr);
         return d.getFullYear() + '-'
            + pad(d.getMonth() + 1) + '-'
            + pad(d.getDate()) + ' T '
            + pad(d.getHours()) + ':'
            + pad(d.getMinutes()) + ':'
            + pad(d.getSeconds());
      }
      let ratio = lbState.limitRatio (photo.w0 / photo.h0);
      let h = lbState.imgHeight;
      let w = Math.round(h * ratio);

      function round2(v) {
         return v ? v.toFixed(2) : '';
         //return Math.round((v + Number.EPSILON) * 100) / 100;
      }
      function stripExplain(e) {
         if (e.startsWith("score=")) {
            let ix = e.indexOf(',');
            if (ix > 0) e = e.substring(ix + 2);
         }
         return e;
      } 
      sb.push('<div class="lb-item cursor_pointer" data-src="');
      sb.push(photo.imgUrl);
      sb.push('" data-photo="');
      sb.push(photo.photoUrl);
      sb.push('"><div class="lb-wrapper"><img class="lb-image lazyload" width="' + w);
      sb.push('" height="' + h + '" src="data:image/gif;base64,R0lGODdhAQABAPAAAMPDwwAAACwAAAAAAQABAAACAkQBADs=" data-src="');
      sb.push(photo.imgUrl);
      sb.push('"><div class="face_name">'); 
      if (photo.names) {
         for (let nameObj of photo.names) {
            let name = nameObj.name || 'unknown';
            sb.push(name + '<br />[' + round2(nameObj.match_score) + ']<br />');
            if (nameObj.explain) {
               sb.push(stripExplain(nameObj.explain) + '<br />');
            }
         }
      } else {
         if (photo.explain) sb.push(", " + photo.explain + "<br />");
      }
      sb.push(photo.src + ", " + photo.id.substring(1+photo.id.lastIndexOf('~')) + "/" + photo.count);
      sb.push(", h=" + photo.h0);
      sb.push(", rto=" + round2(photo.face_ratio));
      sb.push(", " + (photo.face_ok ? 'ok' : 'not ok') + "<br />");
      sb.push(photo.storage_id + ", " + toDateString(photo.updated));
      sb.push('</div>');

      //<div class="txtbadge info-badge bottom-right-4"></div>
      sb.push('</div></div>');
   }

   //Initializes the lightbox style and returns a LightboxState
   function _prepareLightboxAndGetHeight($elt, _ratio) {
      //console.log("PREPARE LB (w, cw, w, pw)", $elt.width(), $elt[0].clientWidth, $elt.width() - 9, $elt.parent()[0].clientWidth);
      const w = $elt.width() - 9; //width without an eventual scrollbar
      if (w <= 0) return; //Probably hidden
      
      const lightboxSizes = _state.lightbox_settings;;
      let sizeSettings;

      //Search correct settings, based on device type and width
      for (let i = 0; i < lightboxSizes.length; i++) {
         if ((lightboxSizes[i].device & _device) == 0) continue;

         //Correct device type
         let sizes = lightboxSizes[i].sizes;
         let j = sizes.length - 1;
         //Search correct width entry. The 1st entry should have width=0
         for (; j >= 0; j--) if (w >= sizes[j].width) break; 
         sizeSettings = sizes[j];
         break;
      }

      //Set the associated styles
      for (const [k, v] of Object.entries(sizeSettings.attr)) {
         $elt.css(k, v);
      }

      let h = sizeSettings.fixed;
      if (h <= 0) {
         h = 240;
         let ratio = _ratio;
         if (ratio < sizeSettings.ratio_lo) ratio = sizeSettings.ratio_lo;
         else if (ratio > sizeSettings.ratio_hi) ratio = sizeSettings.ratio_hi;

         let n = sizeSettings.target_count;
         if (n <= 0) {
            n = 4;
            if (w < 512) n = 2;
            else if (w < 1024) n = 3;
            if (ratio > .95 && ratio < 1.05) n++;
         }

         let availableW = Math.floor(w / n) - 4; //4 because of margin
         console.log("w, n, availableW=", w, n, availableW);
         let availableH = availableW / ratio;
         if (availableH < h) h = Math.floor(availableH);
         console.log("availableW, h, ratio, n=", availableH, h, ratio, n);
      }
      if (_faceMode && h < 200) h = 200;
      return new LightboxState(h, sizeSettings, _ratio);
   }

   function _resizeHandler(ev) {
      if (ev.target !== window) return;
      if (!_faceMode && (!_lg || !_lg.el)) return;

      if (!_faceMode) _setGalleryTitle();
      let $elt = $("#lightbox");
      const lbState = _prepareLightboxAndGetHeight($elt, _lastLbState.ratio);
      if (lbState && _lastLbState.isChanged(lbState)) {
         console.log('RESIZE changed states: old=', _lastLbState);
         _updateLightBox("resize");
      }
   }

   function _getMetaTable(ix) {
      let sb = [];

      function addRow(k, v) {
         if (v) sb.push("<tr><td>" + k + ":</td><td>" + v + "</td?</tr>");
      }
      let photo = _lg.galleryItems[ix].file;
      let ix1 = photo.f.lastIndexOf('\\');

      sb.push("<table class='meta_table'>");
      addRow("Gevonden", photo.terms);
      addRow("File", photo.f.substring(ix1 + 1));
      addRow("Album", photo.a);
      addRow("Datum", photo.date);
      addRow("Titel", photo.t_nl);
      addRow("Ocr", photo.t_ocr);
      addRow("Tit_en", photo.t_en);
      addRow("Text", photo.t_txt);
      addRow("Camera", photo.c);
      if (_state.debug || _state.is_local) {
         addRow("Dir", photo.f.substring(0, ix1));
         addRow("Sortkey", photo.sk);
      }
      if (photo.names) {
         let lines = [];
         for (let i = 0; i < photo.names.length; i++) {
            let name = photo.names[i];
            lines.push(name.name + " (" + name.match_score.toFixed(2) + ")");
         }
         addRow("Namen", lines.join("<br />"));
      }
      if (photo.score) addRow("Score", photo.score.toFixed(2));
      if (photo.explain) addRow("Explain", photo.explain.join("<br />"));
      sb.push("</table>");

      return sb.join('');
   }

   function _detectTouch(ev) {
      _isTouch = true;
      $(window).off('touchstart', _detectTouch);
      console.log("touch event: ", ev.originalEvent.type);
   }

   let _openedInfoViaClick;
   function _infoHandler(ev) {
      const $evTarget = $(ev.target);
      const isClick = (ev.type === 'click');

      function openCurrentInfo($tgt) {
         let ix = _lg.lgOpened ? _lg.index : $tgt.closest('.lb-item').index();
         console.log("INFO: ix=", ix);
         _showInfo(ix, isClick, $tgt);
      }
      let $target;
      if (isClick) {
         $target = $evTarget.closest('.info-badge');
         if ($target.length > 0) {
            ev.preventDefault();
            ev.stopImmediatePropagation(); console.log('INFO:preventDefault');
            _openedInfoViaClick = true;
            return openCurrentInfo($target);
         }
         _openedInfoViaClick = false;
         console.log("INFO: Closing panel by click");
         _overlay.hideNow();
         return;
      } else if (_isTouch)
         return;

      //Not a click, not touch
      $target = $evTarget.closest('.info-badge');
      if ($target.length === 0) {
         $target = $evTarget.closest('.ovl');
         if ($target.length === 0) {
            if (!_openedInfoViaClick) {
               console.log("INFO: Closing panel by mouseout");
               _overlay.hideNow();
            }
         }
         return;
      }

      _openedInfoViaClick = false;
      openCurrentInfo($target);
   }

   function _showInfo(ix, isClick, $target) {
      if ($target === undefined) $target = $($('.lb-item').get(ix)).find('.info-badge');
      console.log('_showInfo', $target);
      
      _overlay.activate($target, { //.closest('.lb-wrapper')
         delay: 1000,
         closeOnClick: true,
         propagateClick: false,
         mode: "scroll",
         initialState: 'fixed',
         debug: dbg_overlay,
         applyExtraStyles: function ($div) {
            $div.css("font-size", "inherit");
         },
         copyContent: function ($dst) {
            let data = _getMetaTable(ix);
            $dst.html(data);
            return data;
         }
      }, isClick ? 0 : undefined);
   }

   function _selectOptionByText($elt, valueToSelect) {
      console.log('select option by value: ', valueToSelect);
      if (valueToSelect) {
         let options = $elt[0].options;
         for (let i = 0; i < options.length; i++) {
            if (options[i].text == valueToSelect) {
               $elt[0].selectedIndex = i;
               break;
            }
         }
      }
   }

   function _setTitle(data, newState) {
      let sb = [];
      if (newState.q) sb.push(newState.q);
      else if (newState.pin) sb.push('Rondom pin');
      sb.push(_faceMode ? "Gezichten" : "Foto's");

      document.title = sb.join(" | ");
   }

   function _setTitleForSlide(elt) {
      let t = elt.fn;
      let ix = t.lastIndexOf('.');
      if (ix > 0 && ix >= t.length - 5)
         t = t.substring(0, ix);
      document.title = t + " | Foto";
   }

   function _indicateLoading($elt) {
      $elt.addClass('lb-loading');
      let intervalsTogo = 30;
      let interval = setInterval(function () {
         let imgs = $elt.find('img');
         if (--intervalsTogo < 0) {
            clearInterval(interval);
            return;
         }
         for (let i = 0; i < imgs.length; i++) {
            let img = imgs[i];
            if (img.complete && !img.src.startsWith ('data:')) {
               console.log('img complete', i, img.src);
               clearInterval(interval);
               $elt.removeClass('lb-loading');
               break;
            }
         }

      }, 500);
   }

   function _buildFilteredFaceNames(expr) {
      if (!expr) expr = $("#face_filter").val();
      if (!expr) expr = ".";
      console.log("Build names for expr=", expr);
      const re = new RegExp(expr, "i");
      const names = _faceNames.sortedNames;
      let arr = [];
      arr.push('<ul>')
      for (let i = 0; i < names.length; i++) {
         const name = names[i];
         if (name.id >= 0 && !re.test(name.name)) continue;
         arr.push('<li data_id="' + name.id + '">' + name.name + '</li>');
      }
      arr.push('</ul>')
      const $div = $('#face_list');
      $div.html(arr.join(''));

      //Setup dragging handler
      $div.find("ul>li").on('pointerdown', _faceDragHandler);
   }

   function _loadFaceNames(fn_follow_up) {
      app.getJSON('facephoto/names', undefined, function (data) {
         let obj = { chgid: data.chgid };
         let src = data.names;
         let dst = {};
         for (let i = 0; i < src.length; i++) {
            dst[src[i].id] = src[i];

         }
         obj.names = dst;
         obj.sortedNames = src;
         _faceNames = obj;
         if (fn_follow_up) fn_follow_up();

         _buildFilteredFaceNames()
      });
   }

   function _updateLightboxPhotos($elt, data) {
      let sb = [];
      let imgUrl = app.createUrl('photo/get') + "&id=";

      //Determine best height of the images and save the value for later usage
      const lbState = _prepareLightboxAndGetHeight($elt, data.max_w_ratio, true);
      _lastLbState = lbState;
      console.log("lbState=", lbState);

      let files = data.files;
      let multipleAlbums = false;
      let album = undefined;
      for (let i = 0; i < files.length; i++) {
         let file = files[i];
         file.imgUrl = imgUrl + encodeURIComponent(file.f);
         if (album === undefined) album = file.a;
         else if (album !== file.a) multipleAlbums = true;
         let ix = file.f.lastIndexOf('\\');
         file.fn = file.f.substring(ix + 1);
         let ix2 = file.f.lastIndexOf('\\', ix - 1);
         if (ix2 >= 0) file.dir = file.f.substring(ix2 + 1, ix);

         _createImgMarkup(sb, file, lbState);
      }
      sb.push("<div class='lb-sentinel-item'></div>")
      $elt.html(sb.join(''));
      _multipleAlbums = multipleAlbums;


      //Unfortunately this event is needed, since otherwise the gallery item would be opened before the infoHandler is called.
      $elt.find('.info-badge').on('click', ev => {
         ev.stopImmediatePropagation();
         ev.preventDefault();
         _infoHandler(ev);
      });

      _prepareTiming();
      _lg.refresh();
      _addTiming(data, "gallery");

      //Make sure the galleryItems contain references to our files
      let galItems = _lg.galleryItems;
      let $window = $(window);
      let dimsSuffix = '&w=' + $window.width() + '&h=' + $window.height();
      for (let i = 0; i < files.length; i++) {
         let galItem = galItems[i];
         let file = files[i];

         galItem.file = file;
         galItem.downloadUrl = galItem.src;
         galItem.download = file.fn;
         galItem.src += dimsSuffix;
      }
   }

   let _photoWindow = null;
   function _updateLightboxFaces($elt, data) {
      let sb = [];
      let imgUrl = app.createUrl('facephoto/get') + "&storid=";
      let photoUrl = app.createUrl('photo/get') + "&id=";
      //let imgUrl = _state.home_url + 'facephoto/get?storid=';
      //let photoUrl = _state.home_url + 'photo/get?id=';

      //Determine best height of the images and save the value for later usage
      const lbState = _prepareLightboxAndGetHeight($elt, data.max_w_ratio || 1, true);
      _lastLbState = lbState;
      console.log("Faces lbState=", lbState);


      let files = data.files;
      for (let i = 0; i < files.length; i++) {
         let file = files[i];
         file.imgUrl = imgUrl + encodeURIComponent(file.storage_id);
         let ix = file.id.lastIndexOf('~');
         file.photoUrl = photoUrl + encodeURIComponent(file.id.substring(0,ix));

         _createFaceMarkup(sb, file, lbState);
      }
      sb.push("<div class='lb-sentinel-item'></div>")
      $elt.html(sb.join(''));

      $elt.find('.lb-item').on('click', function (ev) {
         if (ev.ctrlKey) {
            ev.stopImmediatePropagation();
         } else {
            if (_draggedFaceId > NO_DRAG_ID) return; //Not a click for us
         }
         let url = $(ev.target).closest('.lb-item').attr('data-photo');
         if (url) window.open(url, 'photowindow');
      });
   }

   let _lazyLoader;
   function _updateLightboxContainer($elt, data) {
      if (_lazyLoader) {
         _lazyLoader.destroy();
         _lazyLoader = undefined;
      }
      $elt.empty();
      if (data && data.files && data.files.length>0) _indicateLoading($elt);

      //Handle tooltip for the searchbox
      let $searchBox = $('#searchq');
      let sbTitle = data.all_terms;
      sbTitle = sbTitle ? "Gevonden:\n\t" + sbTitle : '';
      $searchBox.attr('data-title', sbTitle);
      if (sbTitle) {
         var mouseAt = $(':hover').last();
         if (mouseAt && mouseAt[0] === $searchBox[0]) _triggerSearchTooltip();
      }

      if (_faceMode) _updateLightboxFaces($elt, data); else _updateLightboxPhotos($elt, data);

      //Only load images that are visible
      _lazyLoader = lazyload(document.querySelectorAll(".lazyload"));
   }

   function _fillCombo($cb, arr, cur) {
      $cb.empty();
      $cb.append(new Option("Alle", -1));
      let curItem = cur ? cur.toLowerCase() : undefined;
      let selIndex = -1;
      for (let i = 0; i < arr.length; i++) {
         let v = arr[i].v;
         if (v === curItem) selIndex = i + 1;
         $cb.append(new Option(v, i));
      }
      if (selIndex > 0) $cb[0].selectedIndex = selIndex;
   }

   const dashUnderscoreExpr = new RegExp("[\\-_]", "");
   function _contains(s1, s2) {
      if (s1 && s2) {
         let tmp1 = s1.replace(dashUnderscoreExpr, '');
         let tmp2 = s1.replace(dashUnderscoreExpr, '');
         return tmp1.indexOf(tmp2) >= 0 || tmp2.indexOf(tmp1) >= 0;
      }
      return false;
   }
   const num8Expr = new RegExp("^\\d{8}[ \\-_]*", "");
   const num6Expr = new RegExp("^\\d{6}[ \\-_]*", "");
   const extExpr = new RegExp("\\.[^\\.]*$", "");
   const yearExpr = new RegExp("^\\[\\d+\\] ", "");
   function _setGalleryTitle(photo) {
      
      if (!photo) {
         if (!_lg.lgOpened) return;
         photo = _data.files[_lg.index];
         if (!photo) return;
      }
      let maxSpace = _computeMaxGalleryTitleSpace();
      let $titleDiv = $(_lg.$toolbar.firstElement).find('.photo_title');

      let sb = [];
      let tmp1 = photo.c || '';
      let tmp2 = photo.date || '';
      if (tmp2.length > 10) tmp2 = tmp2.substring(0, 10);
      if (tmp1.indexOf('scan') >= 0 || tmp1.indexOf('Scan') >= 0 && tmp2.indexOf('-01-01') > 0) {
         if (photo.year) sb.push(photo.year);
      } else {
         if (tmp2) sb.push(tmp2);
      }

      let dir = photo.dir;
      if (dir == photo.year) dir = '';

      let title = photo.a;
      if (title) title = title.replace(yearExpr, '');
      let fn = photo.fn;
      if (fn) {
         fn = fn.replace(extExpr, '');
         if (tmp2.length === 10) {
            fn = fn.replace(num8Expr, '');
            fn = fn.replace(num6Expr, '');
         } 
         if (fn.startsWith(title)) title = fn;
      }
      if (dir && !_contains(dir, title)) sb.push(dir); 

      //Check words from the filename and append them to the title
      if (fn) {
         console.log("fn=", fn);
         let sb2 = [];
         for (w of fn.split(/[ ,]/)) {
            if (!title.includes(w)) sb2.push(w);
         }
         if (sb2.length > 0) title += ", " + sb2.join(' ');
      }
      sb.push(title);

      tmp2 = sb.join('  -  ');
      $titleDiv.text(tmp2);
      console.log('Max space=', maxSpace, ', w=', $titleDiv.width());
      if ($titleDiv.width() <= maxSpace)
         $titleDiv.removeClass('hidden');
      else
         $titleDiv.addClass('hidden');
   }

   //Compute the max #pixels left for an eventual title of a photo in the toolbar
   function _computeMaxGalleryTitleSpace() {
      let $tb = $(_lg.$toolbar.firstElement);
      let mid = $tb.width() / 2;
      let titleElt = $tb.find('.photo_title')[0];
      let $c = $tb.children();
      let leftMax = 0;
      let rightMin = 2*mid;
      for (let i = 0; i < $c.length; i++) {
         if ($c[i] == titleElt) continue;
         let rc = $c[i].getBoundingClientRect();
         if (rc.x > mid) {
            if (rc.x < rightMin) rightMin = rc.x;
         } else {
            if (rc.right > leftMax) leftMax = rc.right;
         }
      }
      let ret = 2 * Math.min(rightMin - mid, mid - leftMax) - 20;
      //console.log('space:', rightMin - mid, mid - leftMax, ret);
      return ret < 0 ? 0 : ret;
   }

   function _handleSlideChange(idx) {
      _zoomer.setPhoto(_data.files[_lg.index]);
   }
   function _createGallery() {
      _lg = lightGallery(document.getElementById('lightbox'), {
         licenseKey: '1000-0000-000-0000',
         mode: 'lg-fade',
         captions: false,
         lastRow: "hide",
         rowHeight: 500,
         margins: 50,
         preload: 1,
         download: true,
         selector: 'a',
         //exThumbImage: 'thumb', //Where did we put the thumb in the html. Currently not needed
         slideShowInterval: 2500,
         closeOnTap: false,
         plugins: [lgVideo, lgAutoplay, lgFullscreen],   //, lgThumbnail lgZoom,, lgHash
         mobileSettings: {
            showCloseIcon: true,
            closable: true,
            controls: false,
            download: false
         }
      });
      //pw _lg.zoomFromOrigin = false;

      let proto = Object.getPrototypeOf(_lg);
      proto['preload'] = _hookedPreload;

      let $ctr = $(_lg.$toolbar.firstElement).find('.lg-counter');
      $ctr.before('<div class="txtbadge info-badge info-badge-detail"></div><div class="photo_title "></div>');

      let $inner = $(_lg.$inner.firstElement);
      _zoomer = createZoomer($inner);
      $inner
         .on('click', function (ev) {
            let $img = $(ev.target).closest('.lg-current').find('.lg-img-wrap').find('.lg-object');
            if ($img.length === 0) {
               console.log('INNER_CLICK:NO IMG');
               return;
            }
            let rect = $img[0].getBoundingClientRect();
            if (rect.width === 0) {
               console.log('EMPTY rect', $img);
               return;
            }
            let mid = (rect.left + rect.right) / 2;
            console.log('INNER_CLICK:', ev.clientX, ev.clientY, mid, rect.top, rect.bottom, $img);
            if (ev.clientY < rect.top || ev.clientY > rect.bottom) {
               console.log('INNER_CLICK: below/above');
               _lg.closeGallery(false);
               return;
            }

            if (ev.clientX > mid + 20)
               //console.log('INNER_CLICK: right');
               _lg.goToNextSlide(false);
            else if (ev.clientX < mid - 20)
               //console.log('INNER_CLICK: left');
               _lg.goToPrevSlide(false);
         })
         ;
      return _lg;
   }

   function _updateLightBox(from) {
      console.log('_updateLightBox(' + from + ')');
      let $elt = $("#lightbox");
      let needHistory = true;
      if (from === "history") {
         needHistory = false;
         if (!_state.isChanged()) {
            console.log("Skipping _updateLightBox: same state");
            _setTitle(_data, _state);
            return;
         }
      } else if (from === "resize") {  //Only update the container if we are already in the correct state
         needHistory = false;
         if (!_state.isChanged()) {
            _updateLightboxContainer($elt, _data);
            return;
         }
      }

      _prepareTiming();
      _state.getJSON(_faceMode ? 'facephoto/index' : 'photo/index', function (data) {
         _lg.closeGallery(true);
         _addTiming(data, "Trip");
         _resetAll();
         _data = data;
         let newState = data.new_state;
         _setTitle(data, newState);

         console.log('send:', _state);
         console.log('recv:', newState);
         const slide = newState.slide;
         newState.slide = undefined;
         _state.saveActiveState(newState);
         _updateLightboxContainer($elt, _data);
         _addTiming(data, "Build html");


         if (!_faceMode) {
            //Fill albums and years and select the correct one
            _fillCombo($("#albums"), data.albums, newState.album ?? data.cur_album);
            _fillCombo($("#years"), data.years, newState.year);
         }

         _setSort(newState);

         //Propagate the new state back into the UI (if set)
         if (newState.per_album !== undefined) $("#per_album").prop("checked", newState.per_album);
         $("#searchq").val(newState.q || '');
         _toggleHelp();

         console.log('NEED HIST:', needHistory, from, _state.isChanged());
         if (needHistory) _pushHistoryCmd();
         if (!_faceMode && slide) _positionToSlide(slide);
      });
   }

   function _onGalleryOpen(e) {
      _zoomer.setPhotos(_data.files);
   }
   function _onGalleryClose(e) {
      console.log('After CLOSE. needBack=', _lg.needBack, history.state);
      if (history.state && history.state.from === "slide") history.back();
      //pw if (_lg.needBack) history.back();
   }
   function _onGallerySlide(ev) {
      _resetAll();
      $(document.body).removeClass('lg-from-hash');  //re-enable animation. See hash-plugin
      let elt = _data.files[ev.detail.index];
      console.log('goto slide', ev.detail.index, elt.fn);
      _setTitleForSlide(elt);
      _setGalleryTitle(elt);
      _pushHistorySlide(elt.f)
   }

   /* 
    * Hooked preload: fixed repping around array limits and delayed preloading with 0.5 seconds
    */
   function _hookedPreload(index) {
      //console.log("PRELOAD", index, this.settings);
      let self = this;
      setTimeout(function () {
         const N = self.galleryItems.length;
         const preload = 1; //settings will be reset to 0. Bug in LightGallery// self.settings.preload;
         //console.log('Actual preload (index,num)', index, preload);
         for (let i = 1; i <= preload; i++) {
            let nextIndex = index + i;
            if (nextIndex >= N) nextIndex -= N;
            self.loadContent(nextIndex, false);
         }
         for (let i = 1; i <= preload; i++) {
            let nextIndex = index - i;
            if (nextIndex < 0) nextIndex += N;
            self.loadContent(nextIndex, false);
         }
      }, 50);
   }

   //function _dumpHistory(why) {
   //   console.log('History length=', history.length, ", FromPop=", _isFromPopState, ", Why=", why, ', state=', history.state);
   //}

   function _getSlideIndex(slide) {
      if (slide) {
         let files = _data.files;
         for (let i = 0; i < files.length; i++) {
            if (files[i].f === slide) return i;
         }
         slide = slide.toLowerCase();
         for (let i = 0; i < files.length; i++) {
            if (!files[i].f.toLowerCase().endsWith(slide)) continue;
            return i;
         }
      }
      return -1;
   }

   function _positionToSlide(slide) {
      if (typeof slide !== 'number') slide = _getSlideIndex(slide);
      if (slide >= 0) {
         let $items = $("#lightbox").find(".lb-item");
         $(document.body).addClass('lg-from-hash');  //Prevent animation. See hash-plugin
         //_lg.zoomFromOrigin = false;
         _lg.openGallery(slide, $items[slide]);
      } else {
         console.log('No slide, closing gallery');
         $(document.body).removeClass('lg-from-hash');  //re-enable animation. See hash-plugin
         _lg.closeGallery();
      }
   }

   function _pushHistoryCmd() {
      if (_state.mode === 'photo') _state.mode = 'photos';
      _state.pushHistory('lb');
      console.log('PUSHed cmd');
   }

   function _pushHistorySlide(slide) {
      if (slide !== undefined) {
         _state.slide = slide;
         let hs = history.state ?? {}; 
         _state.pushHistory('slide', hs.from==='slide');

         console.log('PUSHed slide');
         _lg.needBack = true;
      }
   }

   function _onPopHistory(ev) {
      _syncState();
      let histState = ev.originalEvent.state || {};
      console.log("OnPopHistLB:", histState);
      if (_state.slide) {
         _positionToSlide(_state.slide);
      } else {
         _lg.needBack = false;
         _positionToSlide();
         _updateLightBox("history");
      }
      return true;
   }

   function _setSort(newState) {
      console.log('set sort', newState.sort, newState);
      if (!newState.sort) return;
      $('#cb_sort').val(newState.sort);
   }

   function _initSortCombo() {
      var $cb = $('#cb_sort');
      if ($cb.length === 0) return;
      if ($cb.find('option').length > 0) return;

      var sortModes = _state.sortmodes;
      for (var prop in sortModes) {
         $cb.append($('<option>', { value: prop, text: sortModes[prop] }));
      }
      _setSort(_state);
      $cb.on('change', function () {
         console.log('changed sort:', this.value);
         if (_lg) _lg.needBack = true;
         _state.sort = this.value;
         _updateLightBox();
      });
   }

   function _toggleHelp() {
      if ($('#searchq').val().trim()==='') {
         $(".help").removeClass("hidden");
      } else {
         $(".help").addClass("hidden");
      }
   }


   function _search() {
      //Don't honor the album and year facet: its really confusing somethimes
      //In case of a query, the per_album setting is ignored
      let q = $("#searchq").val();
      _state.q = q;
      _state.pin = undefined;
      _state.per_album = q ? undefined : $("#per_album")[0].checked;
      _state.album = undefined;
      _state.year = undefined;
      _state.slide = undefined;
      _updateLightBox();
   }

   let _overlay = createOverlay('#overlay');

   $('#albums').on('change', function () {
      let ix = parseInt(this.value);
      _state.slide = undefined;
      _state.album = ix < 0 ? undefined : _data.albums[ix].v;
      _updateLightBox();
   });
   $('#years').on('change', function () {
      let ix = parseInt(this.value);
      _state.slide = undefined;
      _state.year = ix < 0 ? undefined : _data.years[ix].v;
      _updateLightBox();
   });
   $("#per_album").on('change', function () {
      _state.album = undefined;
      _state.slide = undefined;
      _state.per_album = this.checked;
      _updateLightBox();
   });
   $('#icon_search').on('click', _search);
   $('#searchq').on('keyup', function (e) {
      if (e.key === 'Enter') {
         _search();
         e.preventDefault();
      }
   }).on('input', function (e) {
      _toggleHelp();
      if (!e.originalEvent.inputType && $("#searchq").val() === '') _search(); //Force search when clearing input
   });

   $("#lightbox")[0].addEventListener('lgAfterOpen', function (e) {
      $(".lg-content").removeAttr("style");
      console.log('lgAfterOpen', e);
   });


   function _onMenuClick(ev, context) {
      function encodeFileNameForSearch(f) {
         return encodeURIComponent(f.replace(/[&\(\)\\\-_,;\.]/g, ' '));
      }
      console.log("ONMENU CLICK", context);
      const clickedPhoto = context.photo;
      const clickedId = context.clickedId;
      const ix = context.targetIndex;
      if (clickedId === 'ctx_goto_album') {
         _lg.needBack = false;
         _state.slide = undefined;
         _state.sort = undefined;
         _state.q = undefined;
         _state.pin = undefined;
         _state.year = undefined;
         _state.album = clickedPhoto.a;
         _updateLightBox();
      } else if (clickedId === 'ctx_goto_track') {
         console.log("trkid=", clickedPhoto.trkid, ", ix=", ix);
         let f = clickedPhoto.f;
         let idx = f.lastIndexOf('\\');
         if (idx > 0) f = f.substring(idx + 1);
         window.open(_state.external_tracks_url.format(_unique++, encodeURIComponent(clickedPhoto.trkid + "|" + f)),
            "trackstab");
      } else if (clickedId === 'ctx_find_nearby') {
         _state.slide = undefined;
         _state.sort = "relevance";
         _state.q = 'pin:"' + clickedPhoto.f + '"';
         _state.year = undefined;
         _state.album = undefined;
         _state.per_album = undefined;
         _lg.needBack = false;
         _updateLightBox();
      } else if (clickedId === 'ctx_goto_faces') {
         window.open(app.createUrl('', '&mode=faces&q="' + encodeURIComponent(clickedPhoto.f) + '"'),
            "faces_tab");
      } else if (clickedId === 'ctx_goto_faces_dir') {
         let file = clickedPhoto.f;
         let ix0 = file.lastIndexOf('\\');
         if (ix0 > 0) {
            let tmp = file.substring(ix0 + 1);
            let ix1 = tmp.search(/\d{8}/);
            file = ix1 >= 0 ? file.substring(0, ix0 + ix1 + 9) : file.substring(0, ix0);
         }
         window.open(app.createUrl('', '&mode=faces&q="' + encodeURIComponent(file) + '"'),
            "faces_tab");
      } else if (clickedId === 'ctx_goto_map') {
         if (ev.ctrlKey) {
            window.open(app.createUrl('', '&mode=map&pin=' + encodeURIComponent(clickedPhoto.f)),
               "maps_tab");
         } else {
            _state.mode = 'map';
            _state.pin = { id: clickedPhoto.f, album: clickedPhoto.a, loc: clickedPhoto.l };
            app.start('lb');
         }
      } else if (clickedId === 'ctx_info') {
         _openedInfoViaClick = true;
         _showInfo(ix, true);
      }
   }

   _createGallery();

   const _ctxMenu = createContextMenu($("#lightbox,#lg-inner-1"), ".lb-item,.lg-item", $("#context_menu"), _onMenuClick);
   _ctxMenu.onMenu(function (ev, context) {
      const $target = context.$target; 
      let ix = context.targetIndex;
      if ($target.hasClass('lg-item'))
         context.targetIndex = ix = $target.find('img').data('index');

      if (ix < 0 || ix >= _data.files.length) return false;
      let curPhoto = _data.files[ix];
      console.log('CTXMENU', ix, curPhoto);
      context.photo = curPhoto;

      this.showMenuItem("#ctx_goto_track", _state.external_tracks_url && curPhoto.trkid);
      this.showMenuItem("#ctx_goto_map", curPhoto.l !== undefined);
      this.showMenuItem("#ctx_find_nearby", curPhoto.l !== undefined);
      this.showMenuItem("#ctx_goto_faces", _state.is_local && curPhoto.fcnt);
      console.log("ONMENU", context);
      return true;
   });


   $(window)
      .on('resize', _resizeHandler)
      .on('click', _infoHandler)
      .on('mouseenter', _infoHandler)
      .on('keydown', function (ev) {
         if (ev.target.name === 'input' || ev.target.name === 'select') return;
         if (!ev.ctrlKey) return;
         switch (ev.code) {
            case 'Home': $('#main').scrollTop(0); break;
            case 'End': $('#main').scrollTop(1000000); break;
         }
       })
      ;

   $("#lightbox")
      .on('lgBeforeOpen', _onGalleryOpen)
      .on('lgAfterClose', _onGalleryClose)
      .on('lgAfterSlide', _onGallerySlide)
      ;

   console.log('INIT done. State=', history.state);

   function _hookDbgBadge(idSelection, func) {
      $(idSelection).on("mouseenter", function (ev) {
         ev.stopPropagation();

         _overlay.activate($(ev.target), { 
            mode: "scroll",
            initialState: 'fixed',
            debug: dbg_overlay,
            applyExtraStyles: "emulate_pre_proportional",
            copyContent: function ($dst) {
               this.showState('html');
               let data = func();
               $dst.html(data);
               $dst.find(".clip_button").on("click", createClipboard().oncopy);
               return data;
            }
         });
      });
   }
   _hookDbgBadge("#dbg_timings", function () {
      return (_data.dbg && _data.dbg.timings) ? JSON.stringify(_data.dbg.timings, "\r\n", 2) : '';
      if (!_data.dbg || !_data.dbg.timings) return;

      let sb = ["<div><button class='clip_button' data-cbtarget='#hoovered_data' title='Copy to clipboard'><img src='/images/icon_copy.png' class='center_vertical' /></button><pre id='hoovered_data'>"];
      sb.push("Timings:\r\n");
      sb.push(JSON.stringify(_data.dbg.timings, "\r\n", 2));
      sb.push("</pre></div>");
      return sb.join('');
   });
   _hookDbgBadge("#dbg_esrequest", function () {
      if (!_data.dbg || !_data.dbg.es_request) return;

      let sb = ["<div><button class='clip_button' data-cbtarget='#hoovered_data' title='Copy to clipboard'><img src='/images/icon_copy.png' class='center_vertical' /></button><pre id='hoovered_data'>"];
      sb.push("Query:\r\n");
      sb.push(JSON.stringify(_data.dbg.es_request, "\r\n", 2));
      sb.push("</pre></div>");
      return sb.join('');
   });

   function _triggerSearchTooltip(ev) {
      let title = $("#searchq").attr('data-title');
      if (!title) return;
      if (ev) ev.stopPropagation();
      _overlay.activate($("#searchq"), {
         mode: 'tooltip',
         debug: dbg_overlay,
         applyExtraStyles: 'emulate_pre'
      });
   }
   $("#searchq").on('mouseenter', _triggerSearchTooltip
   ).on('mouseout', function (ev) {
      //_overlay.hideNow();
   });

   let _onChangeTimer = 0;
   if ($("#face_names").length > 0) {
      $("#content").splitter({
         splitterClass: "splitter",
         barNormalClass: "",
         barHoverClass: "",
         barActiveClass: "",
         barLimitClass: "",
         splitVertical: true,
         outline: true,
         sizeLeft: 200,
         resizeTo: window
      });
      $("#face_filter").on("input", function () {
         if (_onChangeTimer) clearTimeout(_onChangeTimer);
         _onChangeTimer = setTimeout(function () {
            _buildFilteredFaceNames();
         }, 100);
      });
   }

   const NO_DRAG_ID = -3;
   let _draggedFaceId = NO_DRAG_ID;
   function _faceDragHandler(ev) {
      function stopDragging() {
         //$(".lb-image").css("cursor");
         document.onmousemove = null;
         document.onmousedown = null;
         _draggedFaceId = NO_DRAG_ID;
         proxy.style.visibility = "hidden";
         $allLI.removeClass("dragged_name");
      }
      ev.preventDefault();
      ev.stopPropagation();
      let $proxy = $("#drag_proxy");
      let proxy = $proxy[0];

      let $target = $(ev.target);
      let $allLI = $target.parent().find("li");
      let idx = $target.attr("data_id");
      console.log("NameIDX=", idx);

      if (idx <= NO_DRAG_ID) return; //Not on a name-LI 

      //OK, pointerdown on a LI
      $allLI.removeClass("dragged_name");
      if (ev.ctrlKey) { //Ctrl-click will search for this face
         _state.slide = undefined;
         _state.mode = "faces";
         _state.sort = "score_down";
         _state.q = "nameid:" + idx;
         _updateLightBox();
         return;
      }
      if (idx == _draggedFaceId) return stopDragging();

      _draggedFaceId = idx;
      $(".lb-image").css("cursor", "pointer");
      $target.addClass("dragged_name");
      let faceName = ev.target.textContent
      $proxy.text(faceName);
      proxy.style.visibility = "visible";
      moveAt(ev.pageX, ev.pageY);

      document.onmousemove = function (ev) {
         moveAt(ev.pageX, ev.pageY);
      };
      document.onmousedown = function (ev) {
         if (_draggedFaceId <= NO_DRAG_ID || ev.ctrlKey) return;
         console.log('mousedown: stamp');

         ev.stopImmediatePropagation();
         ev.preventDefault();

         let mouseElt = document.elementFromPoint(ev.clientX, ev.clientY);
         console.log("x,y=", ev.clientX, ev.clientY, mouseElt);
         if (mouseElt) {
            let $elt = $(mouseElt).closest(".lb-item");
            console.log("$elt=", $elt);
            console.log("$parent=", $elt.parent());
            let idx = $elt.parent().find(".lb-item").index($elt[0]);
            console.log("Dropped on ix=", idx);
            console.log(_data.files[idx]);
            if (idx < 0 || idx >= _data.files[idx])
               stopDragging();
            else {
               let file = _data.files[idx];
               let payload = { id: file.id, faceid: _draggedFaceId, correct: ev.altKey };
               _state.postJSON("facephoto/setface", payload, function (data) {
                  console.log("SetFace result: ", data);
                  $elt.find(".face_name").text(faceName);
               });
            }
         }
      };

      function moveAt(pageX, pageY) {
         proxy.style.left = (pageX + 12) + 'px';
         proxy.style.top = (pageY + 12) + 'px';
      }
      console.log('Drag ', ev.target.textContent);
   };

   function _syncState() {
      _faceMode = _state.mode === 'faces';
      _initSortCombo();
   }
   function _start(from) {
      _syncState();
      console.log('Start: hist state=', history.state);
      if (_faceMode) {
         _loadFaceNames(function () {
            _updateLightBox(from);// history.state ? "history" : undefined);
         });
      } else {
         _updateLightBox(from);// history.state ? "history" : undefined);
      }
      return true;
   }

   return {
      start: _start,
      onPopHistory: _onPopHistory
   };
}


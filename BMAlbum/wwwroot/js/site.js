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


function createApplication(state) {
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

   if ((state.debug_flags & 0x10000) !== 0 && bmGlobals.hookConsole)
      bmGlobals.hookConsole([state.home_url, 'clientlog/log?', state.home_url_params].join(''));



   const dbg_overlay = false;
   //NB these constants should match the BrowserType enum in LightboxSettings.cs
   const DESKTOP = 1;
   const PHONE = 2;
   const TABLET = 4;

   let _state = state;
   let _data;
   let _lg;
   let _curDims = [0,0];
   let _lastLbState = new LightboxState(-1, null, 0);  //Last state of the lightbox
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

   function modulo(val, m) {
      return Math.floor(val / m) * m;
   }
   function roundModulo(val, m) {
      return Math.round(val / m) * m;
   }

   function _updateDims() {
      let $window = $(window);
      _curDims[0] = $window.width();
      _curDims[1] = $window.height();
   }
   _updateDims();


   function _createUrlParts(url) {
      return [_state.home_url, url, "?", _state.home_url_params];
   }

   function _createUrl(url, parms) {
      console.log("CreateUrl", url, parms);
      var parts = _createUrlParts(url);
      if (parms instanceof Array) {
         parts = parts.concat(parms);
      } else {
         parts.push(parms);
      }
      console.log("-->", parts.join(""));
      return parts.join("");
   }

   function _getJSON(url, parms, func) {
      let realUrl = _createUrl(url, parms);
      $.ajax({
         dataType: "json",
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

   function _createImgMarkup(sb, photo, lbState) {
      sb.push('<a href="" class="lb-item" data-src="'); //PW:  jg-entry jg-entry-visible
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
      let ratio = lbState.limitRatio (photo.w0 / photo.h0);
      let h = lbState.imgHeight;
      let w = Math.round(h * ratio);

      function round2(v) {
         return v ? v.toFixed(2) : '';
         //return Math.round((v + Number.EPSILON) * 100) / 100;
      }
      sb.push('<div class="lb-item cursor_pointer" data-src="'); //PW:  jg-entry jg-entry-visible
      sb.push(photo.imgUrl);
      sb.push('" data-photo="');
      sb.push(photo.photoUrl);
      sb.push('"><div class="lb-wrapper"><img class="lb-image lazyload" width="' + w);
      sb.push('" height="' + h + '" src="data:image/gif;base64,R0lGODdhAQABAPAAAMPDwwAAACwAAAAAAQABAAACAkQBADs=" data-src="');
      sb.push(photo.imgUrl);
      sb.push('"><div class="face_name">'); 
      if (photo.names) {
         let nameObj = photo.names[0];
         if (nameObj) {
            let name = nameObj.name || 'unknown';
            sb.push(name + '<br />[' + photo.storage_id);
            sb.push(', ' + round2(nameObj.match_score));
            if (nameObj.score_all) {
               sb.push(', ' + round2(nameObj.face_detect_score));
               sb.push(', ' + round2(nameObj.detected_face_detect_score));
               sb.push(', ' + round2(nameObj.score_all));
            }
            sb.push(']');
         }
      }
      sb.push('</div>');

      //<div class="txtbadge info-badge bottom-right-4"></div>
      sb.push('</div></div>');
   }

   //Initializes the lightbox style and returns a LightboxState
   function _prepareLightboxAndGetHeight($elt, _ratio) {
      let w = $elt.width()-9; //9 for the scrollbar
      let sizeSettings;

      //Search correct settings, based on device type and width
      for (let i = 0; i < _lightboxSizes.length; i++) {
         if ((_lightboxSizes[i].device & _device) == 0) continue;

         //Correct device type
         let sizes = _lightboxSizes[i].sizes;
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

         let availableW = (w - n * 4) / n; //4 because of margin
         console.log("w, n, availableW=", w, n, availableW);
         let availableH = availableW / ratio;
         if (availableH < h) h = Math.floor(availableH);
         console.log("availableW, h, ratio, n=", availableH, h, ratio, n);
      }
      if (_state.face_mode && h < 200) h = 200;
      return new LightboxState(h, sizeSettings, _ratio);
   }

   let _resizeTimer;
   function _resizeHandler(ev) {
      if (ev.target !== window) return;
      if (!_state.face_mode && (!_lg || !_lg.el)) return;

      if (_resizeTimer) clearTimeout(_resizeTimer);
      _resizeTimer = setTimeout(function () {
         _updateDims();
         if (!_state.face_mode) _setGalleryTitle();
         let $elt = $("#lightbox");
         const lbState = _prepareLightboxAndGetHeight($elt, _lastLbState.ratio);
         if (_lastLbState.isChanged(lbState)) {
            console.log('RESIZE changed states: old=', _lastLbState);
            _updateLightBox("resize");
         }
      }, 300);
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
      if (_state.debug || _state.extInfo) {
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
      if (newState.year) sb.push(newState.year);
      if (data.cur_album) sb.push(data.cur_album);
      sb.push(_state.face_mode ? "Gezichten" : "Foto's");

      document.title = sb.join(" | ");
   }

   function _setTitleForSlide(elt) {
      let t = elt.fn;
      let ix = t.lastIndexOf('.');
      if (ix > 0 && ix >= t.length - 5)
         t = t.substring(0, ix);
      document.title = t + " | Foto's ";;
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

   function _loadFaceNames(fn_follow_up) {
      _getJSON('facephoto/names', '', function (data) {
         let obj = { chgid: data.chgid };
         let src = data.names;
         let dst = {};
         for (let i = 0; i < src.length; i++) {
            dst[src[i].id] = src[i];
         }
         obj['names'] = dst;
         _faceNames = obj;
         if (fn_follow_up) fn_follow_up();

         let $div = $('#face_names');
         let arr = [];
         arr.push('<ul>')
         for (let i = 0; i < src.length; i++) {
            arr.push('<li data_id="' + src[i].id + '">' + src[i].name + '</li>');
         }
         arr.push('</ul>')
         $div.html(arr.join(''));

         //Setup dragging handler
         $div.find("ul>li").on('pointerdown', _faceDragHandler);
      });
   }

   function _updateLightboxPhotos($elt, data) {
      let sb = [];
      let imgUrl = _state.home_url + 'photo/get?id=';

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

      if (_lg) _lg.refresh();
      else _createGallery($elt, 'a');

      //Make sure the galleryItems contain references to our files
      let galItems = _lg.galleryItems;
      let dimsSuffix = '&w=' + _curDims[0] + '&h=' + _curDims[1];
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
      let imgUrl = _state.home_url + 'facephoto/get?storid=';
      let photoUrl = _state.home_url + 'photo/get?id=';

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

      let dims = {
         sx: screen.width,
         sy: screen.height,
         wx: $(window).width(),
         wy: $(window).height(),
         wiw: window.innerWidth,
         cx: $elt.width()
      };
      console.log('DIMS=' + JSON.stringify(dims));

      if (_state.face_mode) _updateLightboxFaces($elt, data); else _updateLightboxPhotos($elt, data);

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
         //let offset = photo.f_offs || 0;
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
   function _createGallery($elt, selector) {
      _lg = lightGallery($elt[0], {
         licenseKey: '1000-0000-000-0000',
         mode: 'lg-fade',
         captions: false,
         lastRow: "hide",
         rowHeight: 500,
         margins: 50,
         preload: 1,
         download: true,
         selector: selector,
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
         if (_state.cmd === _state.activeCmd) {
            console.log("Skipping _updateLightBox: same cmd");
            return;
         }
      } else if (from === "resize") {  //Only update the container if we are already in the correct state
         needHistory = false;
         if (_state.cmd === _state.activeCmd) {
            _updateLightboxContainer($elt, _data);
            return;
         }
      }

      let parms = [_state.cmd];
      if (_state.u) parms.push('&u=' + _state.u);
      _getJSON(_state.face_mode ? 'facephoto/index' : 'photo/index', parms, function (data) {
         _overlay.hideNow();
         _data = data;
         let newState = data.new_state;
         $("#dbg-label").text(_data.dbg);
         _setTitle(data, newState);

         console.log('send:', _state);
         console.log('recv:', newState);
         _state.activeCmd = _state.cmd = newState.cmd || '';
         _updateLightboxContainer($elt, _data);

         if (!_state.face_mode) {
            //Fill albums and years and select the correct one
            _fillCombo($("#albums"), data.albums, newState.album ? newState.album : data.cur_album);
            _fillCombo($("#years"), data.years, newState.year ? newState.year : data.cur_year);
         }

         _setSort(newState);

         //Propagate the new state back into the UI (if set)
         if (newState.per_album !== undefined) $("#per_album").prop("checked", newState.per_album);
         $("#searchq").val(newState.q || '');


         if (needHistory) {
            _pushHistoryCmd(_state.activeCmd);
         } else console.log("NO push: is from " + from);

         if (!_state.face_mode) {
            let slide = newState.slide;
            if (!slide) slide = _state.slide;
            _state.slide = undefined;
            _positionToSlide(slide);
         }
      });
   }

   function _onGalleryOpen(e) {
      _zoomer.setPhotos(_data.files);
   }
   function _onGalleryClose(e) {
      console.log('After CLOSE. needBack=', _lg.needBack);
      if (_lg.needBack) history.back();
   }
   function _onGallerySlide(ev) {
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
      let self = this;
      setTimeout(function () {
         let N = self.galleryItems.length;
         let preload = self.settings.preload;
         //console.log('Actual preload', index, preload);
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
      }, 500);
   }

   //function _dumpHistory(why) {
   //   console.log('History length=', history.length, ", FromPop=", _isFromPopState, ", Why=", why, ', state=', history.state);
   //}
   function _positionToSlide(slide) {
      if (slide) {
         console.log('Handling slide=', slide);
         let files = _data.files;
         for (let i = 0; i < files.length; i++) {
            if (files[i].f !== slide) continue;
            console.log('found at pos=', i);
            $(document.body).addClass('lg-from-hash');  //Prevent animation. See hash-plugin
            _lg.openGallery(i);
            break;
         }
      } else {
         console.log('No slide, closing gallery');
         $(document.body).removeClass('lg-from-hash');  //Prevent animation. See hash-plugin
         _lg.closeGallery();
      }
   }

   function _pushHistoryCmd(cmd) {
      if (cmd === undefined) cmd = '';
      if (cmd === undefined) {
         console.log('NOT pushing cmd state: ', cmd);
         return;
      }

      let url = new URL(_state.entryUrl);
      url.search = _state.home_url_params + cmd;
      url.hash = '';
      let histState = { cmd: cmd, title: document.title, url: url.href };
      //history.pushState(histState, histState.title, histState.url);
      if (history.state) history.pushState(histState, histState.title, histState.url);
      else history.replaceState(histState, histState.title, histState.url);
      console.log('PUSHed cmd');
   }

   function _pushHistorySlide(slide) {
      if (slide !== undefined) {
         let url = new URL(_state.entryUrl);
         url.search = _state.home_url_params + _state.cmd + '&slide=' + encodeURIComponent(slide);
         url.hash = '';
         let histState = { slide: slide, title:document.title, url: url.href };

         if (history.state && history.state.slide) history.replaceState(histState, histState.title, histState.url);
         else history.pushState(histState, histState.title, histState.url);
         console.log('PUSHed slide');
         _lg.needBack = true;
      }
   }

   function _onPopState(ev) {
      let state = ev.originalEvent.state || {};
      console.log('popped state:', state);
      if (state.slide) {
         _positionToSlide(state.slide);
      } else {
         _lg.needBack = false;
         _positionToSlide();
         _state.cmd = state ? state.cmd : '';
         _updateLightBox("history");
      }
   }

   function _setSort(newState) {
      console.log('set sort', newState.sort, newState);
      if (!newState.sort) return;
      $('#cb_sort').val(newState.sort);
   }

   function _initSortCombo() {
      var $cb = $('#cb_sort');
      if ($cb.length === 0) return;

      var sortModes = state.sortmodes;
      for (var prop in sortModes) {
         $cb.append($('<option>', { value: prop, text: sortModes[prop] }));
      }
      _setSort(state);
      $cb.on('change', function () {
         console.log('changed sort:', this.value);
         if (_lg) _lg.needBack = true;
         _state.cmd += "&sort=" + encodeURIComponent(this.value);
         _updateLightBox();
      });
   }


   //function _resetScroll() {
   //   //let $elt = $('#albums');
   //   //$elt.css("max-width", "50px");

   //   console.log("RESET_SCROLL2");
   //   let $vp = $("meta[name=viewport]");
   //   console.log('VIEWPORT=', $vp);
   //   if ($vp.length <= 0) return;
   //   $vp.attr("content", "width=device-width, user-scalable=no");
   //   console.log("RESET_SCROLL2a");
   //   setTimeout(function () {
   //      console.log("RESET_SCROLL3");
   //      $vp.attr("content", "width=device-width, initial-scale=1");
   //   }, 200);
   //}
   function _search() {
      //Don't honor the album and year facet: its really confusing somethimes
      //In case of a query, the per_album setting is ignored
      let q = $("#searchq").val();
      let perAlbum = q ? '' : $("#per_album")[0].checked;
      _state.cmd = "&q=" + encodeURIComponent(q) + "&per_album=" + perAlbum;
      _updateLightBox();
   }

   let _overlay = createOverlay('#overlay');

   $('#albums').on('change', function () {
      let ix = parseInt(this.value);
      _state.cmd += "&album=" + (ix < 0 ? "" : encodeURIComponent(_data.albums[ix].v));
      _updateLightBox();
   });
   $('#years').on('change', function () {
      let ix = parseInt(this.value);
      _state.cmd += "&year=" + (ix < 0 ? "" : encodeURIComponent(_data.years[ix].v));
      _updateLightBox();
   });
   $("#per_album").on('change', function () {
      _state.cmd += "&per_album=" + this.checked;
      _updateLightBox();
   });
   $('#icon_search').on('click', _search);
   $('#searchq').on('keyup', function (e) {
      if (e.key === 'Enter') {
         _search();
         e.preventDefault();
      }
   });

   $("#lightbox")[0].addEventListener('lgAfterOpen', function (e) {
      $(".lg-content").removeAttr("style");
      console.log('lgAfterOpen', e);
   });

   let _touchTriggered;
   let _touchTimer;
   let _touchStartEvent; 
   function _resetTouchAdmin() {
      console.log('_resetTouchAdmin', _resetTouchAdmin.caller.name);
      _touchTriggered = false;
      _touchStartEvent = undefined;
      if (_touchTimer) {
         clearTimeout(_touchTimer);
         _touchTimer = undefined;
      }
   }
   function _touchStart(ev) {
      _isTouch = true;
      console.log('touchstart', ev.targetTouches.length);
      const $evTarget = $(ev.target);
      if ($evTarget.closest('.bm_menu').length === 0) { //outside a context menu: close 'm all
         $('.bm_menu').removeClass('bm_menu_active');
      } 
      if ($evTarget.closest('#overlay').length === 0) { //outside an overlay: close
         _overlay.hideNow();
      }

      _resetTouchAdmin();
      if (ev.targetTouches.length > 1) return;
      _touchStartEvent = ev;
      console.log('touchstart: setting timer');
      _touchTimer = setTimeout(() => {
         console.log('in timer', _touchTriggered);
         _touchTimer = undefined;
         _touchTriggered = true;
         _contextMenu(ev);
         console.log('timer done', _touchTriggered);
      }, 650);
   }
   function _touchCancel(ev) {
      if (ev.type === 'touchmove') {
         if (Math.abs(ev.pageX - _touchStartEvent.pageX) <= 20 && Math.abs(ev.pageY - _touchStartEvent.pageY) <= 20)
            return;
         console.log('touchMove too far from start');
      } else
         console.log('_touchCancel', _touchTriggered, ev.type);
      _resetTouchAdmin();
   }
   function _touchEnd(ev) {
      console.log('touchend', _touchTriggered);
      if (_touchTriggered) ev.preventDefault();
      _resetTouchAdmin();
   }

   $('.bm_menu')
      .on('click mouseleave', ev => {
         console.log('ctx:click or leave', this, ev);
         $(ev.currentTarget).closest('.bm_menu').removeClass("bm_menu_active");
      })
      .find('.bm_menu_item')
      .on('click', ev => {
         $(ev.currentTarget).closest('.bm_menu').removeClass("bm_menu_active");
      });

   function _contextMenu(ev) {
      const $evTarget = $(ev.target);
      //console.log('handle context menu', ev);
      let ix = -1;
      let $target = $evTarget.closest('.lb-item');
      if ($target.length === 0) {
         $target = $evTarget.closest('.lg-item');
         if ($target.length === 0) return; //Not for us
         ix = $target.find('img').data('index');
      } else
         ix = $target.index();

      //console.log('-- target=', $target, ', ix=', ix);
      if (ix < 0 || ix >= _data.files.length) return;

      let $item = $("#ctx_goto_track");
      if (_data.files[ix].trkid === undefined) $item.addClass("bm_menu_disabled");
      else $item.removeClass("bm_menu_disabled");

      ev.preventDefault();
      console.log("Open context menu. photo-ix:", ix);
      $('#context_menu').data('ix', ix).addClass('bm_menu_active').position({
         my: "left-4px top-4px",
         of: ev,
         collision: "fit"
      });
   }
   $('.bm_menu_item').on('click', ev => {
      ev.preventDefault();
      ev.stopImmediatePropagation();
      console.log("CTX: click on menu");
      let $item = $(ev.target).closest('.bm_menu_item');
      if ($item.hasClass('bm_menu_disabled')) return;

      let ix = $item.closest('.bm_menu').data('ix');
      let clickId = $(ev.currentTarget).closest('.bm_menu_item')[0].id;
      console.log('handle click on', clickId, ix);

      if (clickId === 'ctx_goto_album') {
         _lg.needBack = false;
         _state.cmd += "&q=&sort=&slide=&album=" + encodeURIComponent(_data.files[ix].a);
         _updateLightBox();
      } else if (clickId === 'ctx_goto_track') {
         console.log("trkid=", _data.files[ix].trkid, ", ix=", ix);
         window.open("https://bitmanager.nl/tracks?t=" + _unique++ + "#" + encodeURIComponent(_data.files[ix].trkid),
            "trackstab");
      } else if (clickId === 'ctx_goto_faces') {
         window.open(_createUrl('faces', '&q=' + encodeURIComponent(_data.files[ix].f.replace(/\\/g, ' '))),
            "facestab");
      } else if (clickId === 'ctx_info') {
         _openedInfoViaClick = true;
         _showInfo(ix, true);
      }
   });

   $(window)
      .on('resize', _resizeHandler)
      .on('click', _infoHandler)
      .on('mouseenter', _infoHandler)
      .on('popstate', _onPopState)
      .on('touchstart', _touchStart)
      .on('touchend', _touchEnd)
      .on('touchmove touchcancel gesturestart', _touchCancel)
      .on('contextmenu', _contextMenu)
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

   _initSortCombo();
   console.log('INIT done. State=', history.state);
   setTimeout(function () {
      if (_state.face_mode) {
         _loadFaceNames(function () {
            _updateLightBox(history.state ? "history" : undefined);
         });
      } else {
         _updateLightBox(history.state ? "history" : undefined);
      }
   }, 50);

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

   if ($("#face_names").length > 0) {
      let $content = $("#content");
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
               let parms = ["&id="];
               parms.push(encodeURIComponent(file.id));
               parms.push("&faceid=");
               parms.push(_draggedFaceId);
               _getJSON("facephoto/setface", parms, function (data) {
                  console.log("result: ", data);
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

   return window.globals = {
      getJSON: _getJSON
   };
}


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

function createMapControl(app) {
   const _mapSettings = app.config.map_settings;

   const googleZoomToEsZoom = [
      2,  //0
      2,  //1
      2,  //2
      2,  //3
      2,  //4
      3,  //5
      3,  //6
      4,  //7
      4,  //8
      4,  //9
      5,  //10
      5,  //11
      6,  //12
      6,  //13
      6,  //14
      7,  //15
      7,  //16
      7,  //17
      8,  //18
      8,  //19
      8,  //20
      9,  //21
      9   //22
   ];
   const maxEsZoom = googleZoomToEsZoom[googleZoomToEsZoom.length-1];
   let _map;
   let _markersOnMap = {
      clusters: {},
      photos: {}
   };

   let _gotoCurposDiv;
   let _curposHasCompass = false;
   let _curposElt;
   let _curposMarker;


   //Keep track of the last map-state. We could do that via the URL, but in case of
   //only a foto-url that's ugly. While showing the map, we do serialize them via the URL,
   //so that the url can be pasted into another window.
   let _lastZoom, _lastCenter;

   function _normalizePosition(pos) {
      //console.log('typeof pos1=', typeof pos, pos instanceof google.maps.LatLng);
      if (Array.isArray(pos)) {
         pos = new google.maps.LatLng(pos[0], pos[1]);
      } else if (typeof (pos) === "string") {
         let arr = pos.split(',');
         pos = new google.maps.LatLng(arr[0], arr[1]);
      } else if (!(pos instanceof google.maps.LatLng)) {
         pos = new google.maps.LatLng(pos.lat, pos.lon ?? pos.lng);
      }
      //console.log('typeof pos2=', typeof pos, pos instanceof google.maps.LatLng);
      return pos;
   }

   function _createGroupMarker(cl) {
      const img = document.createElement('img');
      img.src = _state.home_url + 'images/' + _mapSettings.group_pin;
      let tit = cl.count + " photo's";
      if (cl.album) tit = tit + " (" + cl.album + ")";
      const marker = new google.maps.marker.AdvancedMarkerElement({
         map: _map,
         position: _normalizePosition(cl.loc),
         content: img,
         title: tit,
         zIndex: 20
      });
      marker.addListener('click', () => {
         console.log('click groupmarker zoom=', _map.getZoom(), marker);
         _map.setZoom(2 + _map.getZoom());
         _map.panTo(marker.position);
      });
      return marker;
   }

   function _updateCurposMarker(pos, doPan) {
      _curpos = pos;
      if (!_map || !pos) return;
      const curpos = _normalizePosition(pos);
      if (_curposMarker) {
         _curposMarker.position = curpos;
      } else {
         console.log("CREATE curpos");
         const img = document.createElement('img');
         img.id = 'curpos';
         img.src = _getCurlocImg();
         img.addEventListener("click", _onCurposClick);
         _curposElt = img;


         _curposMarker = new google.maps.marker.AdvancedMarkerElement({
            map: _map,
            position: new google.maps.LatLng(pos.lat, pos.lon),
            content: img,
            title: 'Huidige positie',
            zIndex: 20
         });

      }
      console.log("UPD curpos", curpos.lat(), curpos.lng(), _state.pin, doPan);
      if (doPan) _map.panTo(_curposMarker.position);
      return _curposMarker;
   }


   function _firePhoto(ev) {
      if (ev.domEvent) ev = ev.domEvent;
      ev.preventDefault();
      _hideMarkerPhoto();
      const pin = this._pin;

      //Mark the current item
      _createMainPhotoMarker(pin);

      //Update UI
      setTimeout(function () {
         _state.mode = 'photos';
         _state.pin = pin;
         _state.slide = undefined;
         _state.q = undefined;
         _state.album = undefined;
         _state.sort = undefined;
         _state.per_album = undefined;
         app.start('map');
      });
   }
   function _firePhotoSlide(ev) {
      if (ev.domEvent) ev = ev.domEvent;
      ev.preventDefault();
      _hideMarkerPhoto();
      const pin = this._pin;

      //Update UI
      setTimeout(function () {
         _state.mode = 'photos';
         _state.pin = pin;
         _state.slide = pin.id;
         _state.q = undefined;
         _state.album = undefined;
         _state.sort = undefined;
         _state.per_album = undefined;
         app.start('map');
      });
   }
   function _showMarkerPhoto(ev) {
      const pin = this._pin;
      if (!pin || !pin.id) return;

      const $ovl = $("#overlay_map");
      const $img = $ovl.find('img');
      const h = window.innerHeight;
      const w = window.innerWidth;

      const imgSize = Math.min(240, (w + h) / 7).toFixed(0) + 'px';
      $img.css({ width: imgSize, height: imgSize });

      $img[0]._pin = pin;
      if (pin.id) {
         let imgUrl = app.createUrl('photo/get') + "&h=240&id=" + encodeURIComponent(pin.id);
         $img.attr('src', imgUrl);
      }

      const rc = ev.target.getBoundingClientRect();
      const ourH = $ovl.height();

      let styles = {};
      if (w - rc.right > ourH) {
         styles.left = (rc.right + 0) + 'px';
         styles.right = '';
      } else {
         styles.left = (rc.left + 16 - ourH) + 'px';
         styles.right = '';
      }
      if (h - rc.bottom > ourH) {
         styles.top = (rc.bottom + 2) + 'px';
         styles.bottom = '';
      } else {
         styles.top = (rc.top - 2 - ourH) + 'px';
         styles.bottom = '';
      }
      $ovl.css(styles).removeClass('ovl-hidden');
   }
   function _hideMarkerPhoto(ev) {
      $("#overlay_map").addClass('ovl-hidden');
   }

   function _initMarker(marker, pin) {
      marker._pin = pin;
      marker.addListener('click', _firePhoto);
      if (!app.isTouch) {
         marker.addEventListener('mouseover', _showMarkerPhoto);
         marker.addEventListener('mouseout', _hideMarkerPhoto);
      }
      return marker;
   }

   function _createPhotoMarker(pin) {
      const img = document.createElement('img');
      img.src = _state.home_url + 'images/' + _mapSettings.other_pins[pin.color | 0];
      const marker = new google.maps.marker.AdvancedMarkerElement({
         map: _map,
         position: _normalizePosition(pin.loc),
         content: img,
         title: (pin.album ?? pin.id),
         zIndex: 10
      });
      return _initMarker(marker, pin);
   }

   function _createMainPhotoMarker(pin) {
      const img = document.createElement('img');
      img.src = _state.home_url + 'images/' + _mapSettings.selected_pin;
      img.width = 48;
      img.height = 48;
      const marker = new google.maps.marker.AdvancedMarkerElement({
         map: _map,
         title: 'positie geselecteerde foto',
         content: img,
         position: _normalizePosition(pin.loc),
         zIndex: 20
      });
      if (pin.album) marker.title = pin.album + " (geselecteerd)";

      if (_markersOnMap.mainPin !== marker) {
         if (_markersOnMap.mainPin) _markersOnMap.mainPin.setMap(null);
         _markersOnMap.mainPin = marker;
      }

      return _initMarker(marker, pin);
   }

   function _removeUntouchedMarkers(markerDict) {
      const touched = {};
      for (let k of Object.keys(markerDict)) {
         let marker = markerDict[k];
         if (!marker.touched) {
            marker.setMap(null);
            continue;
         }
         marker.touched = false;
         touched[k] = marker;
      }
      //console.log('_removeUntouched: ', Object.keys(markerDict).length, ' -> ', Object.keys(touched).length);
      //console.log('_removeUntouched: ', markerDict, ' -> ', touched);
      return touched;
   }

   function _positionToString(pos) {
      return (typeof pos.lat === 'function') ? pos.lat() + ',' + pos.lng() : pos.lat + ',' + pos.lng;
   }
   function _boundsToString(bounds) {
      let ne = bounds.getNorthEast();
      let sw = bounds.getSouthWest();
      return ne.lat() + "," + sw.lng() + "," + sw.lat() + "," + ne.lng();
   }
   function _stringToBounds(bounds) {
      let arr = bounds.split(',');
      return new google.maps.LatLngBounds(
         new google.maps.LatLng(arr[2], arr[1]), //sw
         new google.maps.LatLng(arr[0], arr[3])  //ne
      );
   }

   let _lastColors = undefined;
   let _skipFetchMarkers;
   function _fetchMarkers() {
      if (_skipFetchMarkers) {
         _skipFetchMarkers = false;
         console.log("skipping _fetchMarkers because advised bounds");
         return;
      }
      let bounds = _map.getBounds();
      let zoom = _map.getZoom();
      console.log('_fetchMarkers: zoom', zoom, 'bounds', bounds);
      if (!bounds) {
         console.log("no bounds!");
         return;
      }
      if (bounds.getNorthEast().lat() == bounds.getSouthWest().lat()) {
         console.log("empty bounds!");
         return;
      }

      let parms = [];
      parms.push("&bounds=" + _boundsToString(_map.getBounds()));
      if (zoom >= 15) parms.push("&mode=photos");
      else {
         //Determine the photo count to switch from clustering to individual photo's
         //This is done by taking the minimum square area in pixels of the div into account
         let elt = document.getElementById("map");
         let minDim = Math.min(elt.clientHeight, elt.clientWidth); //max square area
         let maxCount = Math.max(50, (minDim * minDim) / 2500).toFixed(0);
         console.log("Request clusters for more than ", maxCount, " photos");
         parms.push("&max_count=" + maxCount);
      }

      zoom = (zoom < googleZoomToEsZoom.length) ? googleZoomToEsZoom[zoom] : maxEsZoom;
      parms.push("&zoom=" + zoom);

      app.postJSON('map/clusters', _lastColors, parms, function (json) {
         //Process clusters (groups)
         let markers = _markersOnMap.clusters;
         let clusters = json.clusters;
         _lastColors = json.colors;
         let totBefore = 0;
         let totAfter = 0;
         for (let k in clusters) {
            totBefore++;
            let mainItem = clusters[k];
            if (!mainItem) continue;

            totAfter++;
            mainItem.k = k;
            let limitCnt = mainItem.count / 2;

            let top = calculateAdjacent(k, 'top');
            let bottom = calculateAdjacent(k, 'bottom');
            let right = calculateAdjacent(k, 'right');
            let left = calculateAdjacent(k, 'left');
            let topleft = calculateAdjacent(left, 'top');
            let topright = calculateAdjacent(right, 'top');
            let bottomright = calculateAdjacent(right, 'bottom');
            let bottomleft = calculateAdjacent(left, 'bottom');
            let arr = [top, bottom, right, left, topleft, topright, bottomright, bottomleft];

            for (let j = 0; j < 8; j++) {
               let h = arr[j];
               let hashItem = clusters[h];
               if (!hashItem) continue;
               if (hashItem.count >= limitCnt) continue;

               //collapse entries
               mainItem.count += hashItem.count;
               clusters[h] = undefined;
            }

            if (markers[k]) {
               markers[k].touched = true;
               continue;
            }

            let marker = _createGroupMarker(mainItem);
            marker.touched = true;
            markers[k] = marker;
         }
         _markersOnMap.clusters = _removeUntouchedMarkers(markers);
         console.log('collapse. before=', totBefore, ', after=', totAfter, json);

         //Process individual photos
         markers = _markersOnMap.photos;
         let photos = json.photos;
         for (let k in photos) {
            let mainItem = photos[k];
            if (!mainItem) continue;
            mainItem.id = k;

            if (markers[k]) {
               markers[k].touched = true;
               continue;
            }
            let marker = _createPhotoMarker(mainItem);
            marker.touched = true;
            markers[k] = marker;
         }
         _markersOnMap.photos = _removeUntouchedMarkers(markers);

         if (json.advised_bounds) {
            _skipFetchMarkers = true;
            console.log("ADVISED: ", json.advised_bounds);
            var newBounds = new google.maps.LatLngBounds();
            newBounds.extend(new google.maps.LatLng(json.advised_bounds[0], json.advised_bounds[1]));
            newBounds.extend(new google.maps.LatLng(json.advised_bounds[2], json.advised_bounds[3]));
            _map.fitBounds(newBounds);
         }

         if (history.state && history.state.mode === 'map') _pushHistory();
      });
   }

   //function _createH3Marker(key, cnt) {
   //   const pos = _normalizePosition(h3.cellToLatLng(key));
   //   const marker = new google.maps.marker.AdvancedMarkerElement({//
   //      map: _map,
   //      position: pos,
   //      title: String(cnt),
   //      //icon: {url: "data:image/svg+xml;base64,"+svg, scaledSize: new google.maps.Size(75, 75) },
   //   });
   //   marker.addListener('click', () => {
   //      console.log('click marker', marker);
   //      let parms = ['&pos=' + marker.position.lat + ',' + marker.position.lng];
   //      _getJSON(_state.user + '/map/dump', parms, function (json) {
   //      });
   //   });
   //   return marker;
   //}


   //function _fetchMarkersH3() {
   //   _clearMarkers();
   //   clearTimeout(_reposTimer);
   //   _reposTimer = setTimeout(() => {
   //      console.log('zoom', _map.getZoom(), 'bounds', _map.getBounds(), _map.getBounds().toUrlValue());
   //      //return;
   //      let zoom = _map.getZoom();
   //      let bnds = _map.getBounds().toUrlValue();
   //      let parms = [];
   //      parms.push("&zoom=" + zoom);
   //      parms.push("&bounds=" + bnds);

   //      _getJSON(_state.user + '/map/h3clusters', parms, function (json) {
   //         let clusters = json.clusters;
   //         for (let i = 0; i < clusters.length; i++) {
   //            let marker = _createH3Marker(clusters[i].key, clusters[i].count);
   //            _markersOnMap.push(marker);
   //         }
   //         console.log(json);
   //      });

   //   }, 100);
   //}


   function _onPopHistory(ev) {
      _start('history');
      return true;
   }

   function _pushHistory() {
      _state.zoom = _lastZoom = _map.getZoom();
      _state.center = _lastCenter = _positionToString(_map.getCenter());

      _state.pushHistory('map', history.state && history.state.mode === 'map');
      console.log('PUSHed map hist');
   }

   let _compassRequested;
   function _getLocation() {
      const sensors = app.sensors();
      if (!_compassRequested) {
         _compassRequested = true;
         const compassSettings = _mapSettings.compass;
         if (compassSettings.active) {
            sensors.initializeCompass(compassSettings.silent ? null : sensors.alertingErrorCallback);
         }
      }

      const gpsSettings = _mapSettings.gps;
      if (gpsSettings.active) {
         const req = {
            errorCB: gpsSettings.silent ? null : sensors.alertingErrorCallback,
            fine: gpsSettings.fine,
            max_cache_secs: gpsSettings.max_cache_secs,
         };
         console.log("GetLocation", req, gpsSettings);
         return sensors.getLocation(req);
      }
      console.log("GEO: not active");

   }

   function _gotoCurpos() {
      _updateCurposMarker(_getLocation(), true);

   //   const curloc = _getLocation();
   //   let lat, lon;
   //   if (_curposMarker) {
   //      lat = _curposMarker.position.lat;
   //      lon = _curposMarker.position.lng;
   //   }
   //   console.log('_gotoCurpos', curloc, lat, lon);
   //   if (!curloc || !_map) return;

   //   _map.panTo(_normalizePosition(curloc));
   }

   function _onCurposClick() {
      console.log('curpos clicked');
      app.sensors().initializeCompass();
   }

   function _getCurlocImg() {
      const fn = app.sensors().getHeading() !== undefined ? 'images/curpos_compass2.svg' : 'images/curpos.svg';
      return _state.home_url + fn;
   }
   function _onLocation(ev) {
      const curloc = ev.detail;
      console.log("_onLocation curloc=", curloc);
      _updateCurposMarker(curloc, _state.pin === "current_position");
   }

   function _onCompass(ev) {
      const heading = ev.detail.heading;
      let $curpos = $("#curpos");
      console.log("_onCompass heading=", heading, ", #curpos=", $curpos.length);
      if (!_curposHasCompass) {
         $curpos.attr('src', _getCurlocImg());
         _curposHasCompass = true;
      }
      if ($curpos.length > 0) {
         const style = $curpos[0].style;
         const rot = "rotate(" + heading + "deg)";
         style.webkitTransform = rot;
         style.MozTransform = rot;
         style.transform = rot;
      }
   }

   function _start(from, recursive) {
      //throw 'hola';
      if (!_mapSettings.active) {
         const msg = "Map is not active. Please check your settings.xml";
         alert(msg);
         throw new Error(msg);
      }
      console.log("STARTMAP", from, _map);
      _hideMarkerPhoto();
      document.title = "Kaart | Foto's";
      _state = app.state;

      //If needed, we must fetch the current location here, and not in the init: security
      let curloc;
      if (from === 'lb' && _state.pin === "current_position" ) {
         curloc = recursive ? app.sensors().getCachedLocation() : _getLocation();
      }

      if (!_map) {
         console.log("GOOGLE", typeof google);
         if (!Object.hasOwn(window, 'google') || !Object.hasOwn(window.google, 'maps')) {
            window._initMap = function () {
               console.log("lazy loading:", from);
               app.start(from, true);
            };
            const script = document.createElement('script')
            const src = "https://maps.googleapis.com/maps/api/js?libraries=places,marker&callback=_initMap&key=";
            script.src = src + encodeURIComponent(_mapSettings.key);
            document.body.appendChild(script);
            return false;
         }

         document.addEventListener("bm_location", _onLocation);
         document.addEventListener("bm_compass", _onCompass);

         console.log('create map');
         _map = new google.maps.Map(document.getElementById("map"), {
            mapId: "ALBUM_MAP",
            center: _normalizePosition(_mapSettings.start_position),
            zoom: _mapSettings.start_zoom,
         });

         _map.addListener('idle', _fetchMarkers);

         //Add goto curpos control
         if (_mapSettings.gps.active) {
            const img = document.createElement('img');
            img.src = _state.home_url + 'images/goto_curpos.svg';
            const div = document.createElement('div');
            div.appendChild(img);
            div.id = 'btn_goto_curpos';
            _gotoCurposDiv = div;
            google.maps.event.addDomListener(div, 'click', _gotoCurpos);
            _map.controls[google.maps.ControlPosition.RIGHT_BOTTOM].push(div);
         }
         console.log('map created');
      }


      //Restore zoom and center if needed
      if (!_state.center) {
         _state.center = _lastCenter;
         _state.zoom = _lastZoom;
      }

      let loc, zoom, why;
      if (from === 'history') {
         zoom = _state.zoom ?? _mapSettings.start_zoom;;
         loc = _state.center ?? _mapSettings.start_position;
         why = 'LOC(hist): ';
      } else if (!_state.pin) {
         loc = _state.center ?? _mapSettings.start_position;
         zoom = _state.zoom ?? _mapSettings.start_zoom;
         why = 'LOC(no pin): ';
      } else if (_state.pin === "current_position") {
         if (curloc) _updateCurposMarker(curloc, true);
         loc = curloc ?? _state.center ?? _mapSettings.start_position;
         zoom = _state.zoom ?? _mapSettings.detail_zoom;
         why = 'LOC(curpos): ';
      } else {
         zoom = _state.zoom ?? _mapSettings.detail_zoom;
         _createMainPhotoMarker(_state.pin);
         loc = _state.pin.loc;
         why = 'LOC(pin): ';
      }
      console.log(why, loc, zoom);

      //Now position the map
      if (loc) {
         _map.panTo(_normalizePosition(loc));

         if (typeof (zoom) === "string") zoom = parseInt(zoom, 10);
         _map.setZoom(zoom);

         //Sometimes the map isn't updated completely
         //also the bounds have a same value for hi- and lo-lat
         //Hide/show forces a refresh
         $("#map").hide().show(0); //Forces a repaint in the map
         //google.maps.event.trigger(_map, 'resize');
      }
      if (from !== 'history') _pushHistory();

      return true;
   }

   $("#overlay_img").on('click', _firePhotoSlide);

   return {
      start: _start,
      onPopHistory: _onPopHistory
   };
}


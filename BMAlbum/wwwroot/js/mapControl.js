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
   const maxGoogleZoom = 13;
   const maxEsZoom = googleZoomToEsZoom[maxGoogleZoom];
   let _map;
   let _markers = {
      zoom: -1,
      clusters: {},
      photos: {}
   };

   let _lastAlbum, _lastZoom;

   function _normalizePosition(pos) {
      //console.log('typeof pos1=', typeof pos, pos instanceof google.maps.LatLng);
      if (Array.isArray(pos)) {
         pos = new google.maps.LatLng(pos[0], pos[1]);
      } else if (typeof (pos) === "string") {
         let arr = pos.split(',');
         pos = new google.maps.LatLng(arr[0], arr[1]);
      } else if (!(pos instanceof google.maps.LatLng)) {
         pos = new google.maps.LatLng(pos.lat, pos.lon | pos.lng);
      }
      //console.log('typeof pos2=', typeof pos, pos instanceof google.maps.LatLng);
      return pos;
   }

   function _createGroupMarker(cl) {
      const img = document.createElement('img');
      img.src = _state.home_url + 'images/' + _state.map_settings.group_pin;
      let tit = cl.count + " photo's";
      if (cl.album) tit = tit + " (" + cl.album + ")";
      const marker = new google.maps.marker.AdvancedMarkerElement({
         map: _map,
         position: _normalizePosition(cl.loc),
         content: img,
         title: tit,
         zIndex:20
      });
      marker.addListener('click', () => {
         console.log('click groupmarker zoom=', _map.getZoom(), marker);
         _map.setZoom(2 + _map.getZoom());
         _map.panTo(marker.position);
      });
      return marker;
   }

   function _firePhoto() {
      _hideMarkerPhoto();
      const pin = this._pin;

      //Mark the current item
      _createMainPhotoMarker(pin);

      //Update UI
      setTimeout(function () {
         _state.mode = 'photos';
         _state.pin = pin;
         _state.q = undefined;
         _state.album = undefined;
         _state.sort = undefined;
         _state.per_album = undefined;
         app.start('map');
      });
   }
   function _firePhotoSlide() {
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
      img.src = _state.home_url + 'images/' + _state.map_settings.other_pins[pin.color | 0];
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
      img.src = _state.home_url + 'images/' + _state.map_settings.selected_pin;
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

      if (_markers.mainPin !== marker) {
         if (_markers.mainPin) _markers.mainPin.setMap(null);
         _markers.mainPin = marker;
      }

      return _initMarker(marker, pin);
   }

   function _removeUntouchedMarkers() {
      function getTouched(coll) {
         touched = {};
         for (let k of Object.keys(coll)) {
            let item = coll[k];
            if (!item.touched) {
               item.setMap(null);
               continue;
            }
            item.touched = false;
            touched[k] = item;
         }
         return touched;
      }
      _markers.clusters = getTouched(_markers.clusters);
      _markers.photos = getTouched(_markers.photos);
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

   let _reposTimer;
   let _lastColors = undefined;
   function _fetchMarkers() {
      console.log("_fetchMarkers");
      clearTimeout(_reposTimer);
      _reposTimer = setTimeout(() => {
         let bounds = _map.getBounds();
         let zoom = _map.getZoom();
         console.log('delayed _fetchMarkers: zoom', zoom, 'bounds', bounds);
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
            let maxCount = Math.max(50, (minDim * minDim) / 3000).toFixed(0);
            console.log("Request clusters for more than ", maxCount, " photos");
            parms.push("&max_count=" + maxCount);
         }

         zoom = (zoom < maxGoogleZoom) ? googleZoomToEsZoom[zoom] : maxEsZoom;
         parms.push("&zoom=" + zoom);

         app.postJSON('map/clusters', _lastColors, parms, function (json) {

            //Process clusters (groups)
            let markers = _markers.clusters;
            let clusters = json.clusters;
            _lastColors = json.colors; //if (json.colors)
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
               let right =  calculateAdjacent(k, 'right');
               let left =  calculateAdjacent(k, 'left');
               let topleft =  calculateAdjacent(left, 'top');
               let topright =  calculateAdjacent(right, 'top');
               let bottomright =  calculateAdjacent(right, 'bottom');
               let bottomleft =  calculateAdjacent(left, 'bottom');
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

               if (zoom === _markers.zoom) {
                  if (markers[k]) {
                     markers[k].touched = true;
                     continue;
                  }
               }
               let marker = _createGroupMarker(mainItem);
               marker.touched = true;
               markers[k] = marker;
            }
            console.log('collapse. before=', totBefore, ', after=', totAfter, json);

            //Process individual photos
            markers = _markers.photos;
            let photos = json.photos;
            for (let k in photos) {
               let mainItem = photos[k];
               if (!mainItem) continue;
               mainItem.id = k;

               if (zoom === _markers.zoom) {
                  if (markers[k]) {
                     markers[k].touched = true;
                     continue;
                  }
               }
               let marker = _createPhotoMarker(mainItem);
               marker.touched = true;
               markers[k] = marker;
            }
            _markers.zoom = zoom;
            _removeUntouchedMarkers();

            if (history.state && history.state.mode==='map') _pushHistory();
         });

      }, 100);
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
   //            _markers.push(marker);
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
      _state.zoom = _map.getZoom();
      _state.center = _positionToString(_map.getCenter());

      //Save the lastZoom in order to be able to use the same zoom for a photo from the same album
      if (_state.pin && _state.pin.album) {
         _lastAlbum = _state.pin.album;
         _lastZoom = _state.zoom;
      } else
         _lastAlbum = null;

      _state.pushHistory('map', history.state && history.state.mode === 'map');
      console.log('PUSHed map hist');
   }

   function _start(from) {
      console.log("startmap", from, _map);
      _hideMarkerPhoto();
      document.title = "Kaart | Foto's";
      _state = app.state;
      if (!_map) {
         console.log("GOOGLE", typeof google);
         if (!Object.hasOwn(window, 'google') || !Object.hasOwn(window.google,'maps')) {
            window._initMap = function () {
               console.log("lazy loading:", from);
               app.start(from);
            };
            const script = document.createElement('script')
            const src = "https://maps.googleapis.com/maps/api/js?libraries=places,marker&callback=_initMap&key=";
            script.src = src + encodeURIComponent(_state.map_settings.key);
            document.body.appendChild(script);
            return false;
         }
         console.log('create map');
         _map = new google.maps.Map(document.getElementById("map"), {
            center: _normalizePosition(_state.map_settings.start_position),
            zoom: _state.map_settings.start_zoom,
            mapId: "ALBUM_MAP"
         });
         _map.addListener('bounds_changed', _fetchMarkers);
         console.log('map', _map);
      }

      let zoom = -1;
      let loc = null;
      if (_state.pin) {
         zoom = maxGoogleZoom;
         _createMainPhotoMarker(_state.pin);
         loc = _state.pin.loc;
         //if (_lastZoom && _lastAlbum === _state.pin.album) zoom = _lastZoom;
         if (_lastZoom) zoom = _lastZoom;
      }

      if (_state.center) loc = _state.center;
      if (_state.zoom) zoom = _state.zoom;

      //Now position the map
      if (loc != null) {
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


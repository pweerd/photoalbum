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

   let _getJSON = app.getJSON;
   let _postJSON = app.postJSON;
   let _needHistory;

   function _normalizePosition(pos) {
      //console.log('typeof pos1=', typeof pos, pos instanceof google.maps.LatLng);
      if (Array.isArray(pos)) {
         pos = new google.maps.LatLng(pos[0], pos[1]);
      } else if (typeof (pos) === "string") {
         let arr = pos.split(',');
         pos = new google.maps.LatLng(arr[0], arr[1]);
      } if (!(pos instanceof google.maps.LatLng)) {
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
         title: tit
      });
      marker.addListener('click', () => {
         console.log('click groupmarker zoom=', _map.getZoom(), marker);
         _map.setZoom(2 + _map.getZoom());
         _map.panTo(marker.position);
      });
      return marker;
   }

   function _firePhoto() {
      let thisLoc = _positionToString(this.position);
      let loc = this.photo_id;
      if (!loc) loc = thisLoc;

      //Mark the current item
      _createMainPhotoMarker(thisLoc, this);
      _pushHistory();

      //Update UI
      setTimeout(function () {
         app.start({
            mode: 'photos',
            pin: loc
         });
      });
   }
   function _createPhotoMarker(cl) {
      const img = document.createElement('img');
      img.src = _state.home_url + 'images/' + _state.map_settings.other_pins[cl.color | 0];
      let tit = cl.album;
      if (!tit) tit = cl.photo_id;
      const marker = new google.maps.marker.AdvancedMarkerElement({
         map: _map,
         position: _normalizePosition(cl.loc),
         content: img,
         title: tit
      });
      marker.photo_id = cl.photo_id;
      marker.album = cl.album;
      marker.addListener('click', _firePhoto);
      return marker;
   }
   function _createMainPhotoMarker(loc, parms) {
      const img = document.createElement('img');
      img.src = _state.home_url + 'images/' + _state.map_settings.selected_pin;
      img.width = 48;
      img.height = 48;
      const marker = new google.maps.marker.AdvancedMarkerElement({
         map: _map,
         title: 'positie geselecteerde foto',
         content: img,
         position: _normalizePosition(loc),
         zIndex: 999
      });
      if (parms) {
         if (parms.photo_id) {
            marker.photo_id = parms.photo_id;
            marker.addListener('click', _firePhoto);
         }
         if (parms.album) marker.title = parms.album + " (geselecteerd)";
      }
      return _setMainMarker(marker);
   }

   function _setMainMarker(marker) {
      if (_markers.mainPin !== marker) {
         if (_markers.mainPin) _markers.mainPin.setMap(null);
         _markers.mainPin = marker;
      }
      return marker;
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

         if (zoom < maxGoogleZoom) {
            zoom = googleZoomToEsZoom[zoom];
         } else {
            zoom = maxEsZoom
            parms.push("&mode=photos");
         }
         parms.push("&zoom=" + zoom);

         _postJSON('map/clusters', _lastColors, parms, function (json) {

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
               mainItem.photo_id = k;

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
      _start(ev.originalEvent.state, 'history');
      return true;
   }

   function _pushHistory() {
      let url = new URL(_state.entryUrl);
      url.search = _state.home_url_params + _state.cmd;
      url.hash = '';
      let histState = {
         mode: _state.mode,
         cmd: _state.cmd,
         title: document.title,
         url: url.href,
         center: _positionToString(_map.getCenter()),
         zoom: _map.getZoom()
      };
      if (_markers.mainPin) {
         histState.pin = _positionToString(_markers.mainPin.position);
         histState.album = _markers.mainPin.album;
         histState.photo_id = _markers.mainPin.photo_id;
      }

      let pushHist = history.replaceState;
      if (history.state && history.state.mode !== 'map') pushHist = history.pushState;
      pushHist.call(history, histState, histState.title, histState.url); 
      console.log('PUSHed map hist');
   }



   function _start(parms, from) {
      console.log("startmap", parms, from, _map);
      document.title = "Kaart | Foto's";
      _state = app.state;
      if (!_map) {
         console.log("GOOGLE", typeof google);
         if (!Object.hasOwn(window, 'google') || !Object.hasOwn(window.google,'maps')) {
            window._initMap = function () {
               console.log("lazy loading:", parms, from);
               app.start(parms, from);
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
      if (parms && parms.pin) {
         zoom = maxGoogleZoom;
         loc = parms.pin;
         _createMainPhotoMarker(loc, parms);
      } else {
         if (_state.pin) {
            zoom = maxGoogleZoom;
            loc = _state.pin.position;
            _createMainPhotoMarker(loc, _state.pin);
         }
      }

      if (parms) {
         if (parms.center) loc = parms.center;
         zoom = parms.zoom !== undefined ?parms.zoom : maxGoogleZoom;
      }

      //Now position the map
      if (loc != null) {
         _map.panTo(_normalizePosition(loc));
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

   return {
      start: _start,
      onPopHistory: _onPopHistory
   };
}


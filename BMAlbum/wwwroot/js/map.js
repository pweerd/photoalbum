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

function createMapApplication(state) {
   if ((state.debug_flags & 0x10000) !== 0 && bmGlobals.hookConsole)
      bmGlobals.hookConsole([state.home_url, 'clientlog/log?', state.home_url_params].join(''));

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


   let _state = state;

   //Save the original url for using in the history
   _state.entryUrl = new URL(window.location);
   _state.entryUrl.search = ''
   _state.entryUrl = _state.entryUrl.href;
   _state.cmd ||= '';




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

   function _normalizePosition(pos) {
      if (Array.isArray(pos)) {
         pos = new google.maps.LatLng(pos[0], pos[1]);
      } else if (typeof (pos) === "string") {
         let arr = pos.split(',');
         pos = new google.maps.LatLng(arr[0], arr[1]);
      }
      return pos;
   }

   function _createGroupMarker(cl) {
      const img = document.createElement('img');
      img.src = '/images/redgrouppin.svg';
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
   function _createPhotoMarker(cl) {
      const img = document.createElement('img');
      img.src = '/images/redpin.svg';
      let tit = cl.album;
      if (!tit) tit = cl.photo_id;
      const marker = new google.maps.marker.AdvancedMarkerElement({
         map: _map,
         position: _normalizePosition(cl.loc),
         content: img,
         title: tit
      });
      marker.photo_id = cl.photo_id;
      marker.addListener('click', () => {
         alert('clicked on ' + marker.photo_id)
         console.log('click photomarker', marker);
      });
      return marker;
   }
   function _createMainPhotoMarker(loc) {
      const img = document.createElement('img');
      img.src = '/images/redpin.svg';
      img.width = 48;
      img.height = 48;
      const marker = new google.maps.marker.AdvancedMarkerElement({
         map: _map,
         title: 'position of selected photo',
         content: img,
         position: _normalizePosition(loc)
      });
      if (_markers.mainPin) _markers.mainPin.setMap(null);
      _markers.mainPin = marker;
      return marker;
   }

   let _map;
   let _markers = {
      zoom: -1,
      clusters: {},
      photos: {}
   };

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

   let _reposTimer;
   function _fetchMarkers() {
      clearTimeout(_reposTimer);
      _reposTimer = setTimeout(() => {
         console.log('zoom', _map.getZoom(), 'bounds', _map.getBounds(), _map.getBounds().toUrlValue());
         //return;
         _state.user = 'alles';
         let zoom = _map.getZoom();
         let parms = [];
         parms.push("&u=" + _state.user);
         parms.push("&bounds=" + _map.getBounds().toUrlValue());

         if (zoom < maxGoogleZoom) {
            zoom = googleZoomToEsZoom[zoom];
         } else {
            zoom = maxEsZoom
            parms.push("&mode=photos");
         }
         parms.push("&zoom=" + zoom);

         _getJSON(_state.user + '/map/clusters', parms, function (json) {

            //Process clusters (groups)
            let markers = _markers.clusters;
            let clusters = json.clusters;
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
   //      _state.user = 'alles';
   //      let zoom = _map.getZoom();
   //      let bnds = _map.getBounds().toUrlValue();
   //      let parms = [];
   //      parms.push("&u=" + _state.user);
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

   async function _initMap() {
      //quarterly
      console.log('initmap2');
      //const { AdvancedMarkerElement } = await google.maps.importLibrary("marker");
      _map = new google.maps.Map(document.getElementById("map"), {
         center: { lat: 52.09, lng: 5.10 },
         zoom: 10,
         mapId: "DEMO_MAP_ID"
      });
      console.log('initmap3', map);
      if (_state.pin) _createMainPhotoMarker(_state.pin);
      _map.addListener('bounds_changed', _fetchMarkers);

   //   console.log('h3=', h3);
   //   const h3Index = h3.latLngToCell(37.3615593, -122.0553238, 7);
   //   console.log('h3Index=', h3Index);
   }

   return window.globals = {
      getJSON: _getJSON,
      initMap: _initMap
   };
}


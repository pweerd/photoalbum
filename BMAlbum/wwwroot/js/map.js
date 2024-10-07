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




   let _state = state;

   //Save the original url for using in the history
   _state.entryUrl = new URL(window.location);
   _state.entryUrl.search = ''
   _state.entryUrl = _state.entryUrl.href;
   _state.cmd ||= '';



   function _updateDims() {
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

   function _normalizePosition(pos) {
      if (typeof (pos) === "string") {
         let arr = pos.split(',');
         pos = new google.maps.LatLng(arr[0], arr[1]);
      }
      return pos;
   }
   function _createMarker(pos, cnt) {
      const marker = new google.maps.marker.AdvancedMarkerElement({//
         map: _map,
         position: _normalizePosition(pos),
         title: String(cnt),
         //icon: {url: "data:image/svg+xml;base64,"+svg, scaledSize: new google.maps.Size(75, 75) },
      });
      marker.addListener('click', () => {
         console.log('click marker', marker);
         let parms = ['&pos=' + marker.position.lat + ',' + marker.position.lng];
         _getJSON(_state.user + '/map/dump', parms, function (json) {
         });
      });
      return marker;
   }

   let _map;
   let _markers = [];
   const svg = window.btoa(`
  <svg fill="red" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 240 240">
    <circle cx="120" cy="120" opacity=".8" r="70" />    
  </svg>`);


   function _clearMarkers() {
      let m = _markers;
      if (m) {
         for (let i = 0; i < m.length; i++) {
            m[i].setMap(null);
         }
         _markers = [];
      }
   };


   let _reposTimer;
   function _fetchMarkers() {
      _clearMarkers();
      clearTimeout(_reposTimer);
      _reposTimer = setTimeout(() => {
         console.log('zoom', _map.getZoom(), 'bounds', _map.getBounds(), _map.getBounds().toUrlValue());
         //return;
         _state.user = 'alles';
         let zoom = _map.getZoom();
         let bnds = _map.getBounds().toUrlValue();
         let parms = [];
         parms.push("&u=" + _state.user);
         parms.push("&zoom=" + zoom);
         parms.push("&bounds=" + bnds);

         _getJSON(_state.user + '/map/clusters', parms, function (json) {
            let clusters = json.clusters;
            for (let i = 0; i < clusters.length; i++) {
               let marker = _createMarker(clusters[i].loc, clusters[i].count);
               _markers.push(marker);
            }
            console.log(json);
         });

      }, 100);
   }
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
      _map.addListener('bounds_changed', _fetchMarkers);
   }

   return window.globals = {
      getJSON: _getJSON,
      initMap: _initMap
   };
}


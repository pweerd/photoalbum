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


function createSensorsApi() {
   let _geoPermission, _compassPermission;

   Promise.all([
      navigator.permissions.query({ name: "geolocation" }),
      navigator.permissions.query({ name: "accelerometer" }),
      navigator.permissions.query({ name: "magnetometer" }),
      navigator.permissions.query({ name: "gyroscope" }),
   ]).then((results) => {
      console.log('IN THEN');
      _geoPermission = results[0].state;
      let denied = false;
      let prompt = false;
      for (let i = 1; i < 4; i++) {
         switch (results[i].state) {
            case 'prompt': prompt = true; break;
            case 'denied': denied = true; break;
         }
      }
      if (denied) _compassPermission = 'denied';
      else if (prompt) _compassPermission = 'prompt';
      else _compassPermission = 'granted';
      console.log('_geoPermission', _geoPermission);
      console.log('_compassPermission', _compassPermission);
      console.log(results);
   }).catch((err) => {
      console.log('Permissions API error:', err.message);
   });

   function _logErrorsCB(what, err) {
      console.log('Cannot use ' + what + ': ' + err);
   }
   function _alertErrorsCB(what, err) {
      alert('Cannot use ' + what + ': ' + err);
   }

   let _geoLocation = undefined;

   function _getCachedLocation() {
      if (_geoLocation) _geoLocation.cached = true;
      return _geoLocation;
   }

   function _getLocation(req) {
      const _req = {};
      if (Error.captureStackTrace) Error.captureStackTrace(_req);
      if (!req) req = _req;
      _req.fine = req.fine ?? true;
      //Use a minimum of 1 sec to make sure that multiple calls return the same location 
      _req.max_cache_secs = Math.max(req.max_cache_secs ?? 30, 1);
      _req.context = req.context;
      console.log("_getLocation", _req);
      const errorCB = _req.errorCB ?? _logErrorsCB;

      if (!navigator.geolocation) {
         errorCB('location', 'Not supported by browser');
         return;
      }
      if (_geoPermission === 'denied') {
         errorCB('location', 'Not allowed by permissions. Please check your settings.');
         return;
      }
      function onPosition(pos) {
         let coords = pos.coords;

         _geoLocation = {
            lat: coords.latitude,
            lon: coords.longitude,
            timestamp: pos.timestamp,
            request: _req,
         };
         console.log("GEO:onPosition", _geoLocation);
         document.dispatchEvent(new CustomEvent("bm_location", { detail: _geoLocation }));
      }
      function onError(err) {
         console.log("GEO:onError. Code=" + err.code + ", msg=" + err.message, _req.stack);
         errorCB('location', "getCurrentPosition error. Code=" + err.code + ", msg=" + err.message);
      }

      if (!_geoLocation || (Date.now() - _geoLocation.timestamp) / 1000 > _req.max_cache_secs) {
         console.log("REQUESTING GEO");
         navigator.geolocation.getCurrentPosition(onPosition, onError, {
            enableHighAccuracy: _req.fine ? true : false,
            timeout: 5000,
            maximumAge: 0
         });
      } else {
         _geoLocation.cached = true;
         document.dispatchEvent(new CustomEvent("bm_location", { detail: _geoLocation }));
      }
      console.log("GEO:ret:", _geoLocation);
      return _geoLocation;
   }

   const toRadMultiplier = Math.PI / 180;
   let _heading = undefined;
   function _compassHandler(e) {
      let heading = e.webkitCompassHeading;
      if (heading === undefined) {
         if (e.alpha === null || e.beta === null || e.gamma === null) {
            console.log('COMPASS not accessible: values are null.');
            return;
         }
         //See https://stackoverflow.com/questions/18112729/calculate-compass-heading-from-deviceorientation-event-api
         const alphaRad = e.alpha ? e.alpha * toRadMultiplier : 0;
         const betaRad = e.beta ? e.beta * toRadMultiplier : 0;
         const gammaRad = e.gamma ? e.gamma * toRadMultiplier : 0; 

         // Calculate equation components
         const cA = Math.cos(alphaRad);
         const sA = Math.sin(alphaRad);
         const cB = Math.cos(betaRad);
         const sB = Math.sin(betaRad);
         const cG = Math.cos(gammaRad);
         const sG = Math.sin(gammaRad);

         // Calculate A, B, C rotation components
         const rA = - cA * sG - sA * sB * cG;
         const rB = - sA * sG + cA * sB * cG;
         //const rC = - cB * cG;

         // Calculate compass heading
         heading = Math.atan(rA / rB);

         // Convert from half unit circle to whole unit circle
         if (rB < 0) {
            heading += Math.PI;
         } else if (rA < 0) {
            heading += 2 * Math.PI;
         }

         // Convert radians to degrees
         heading /= toRadMultiplier;
      }

      heading = Math.round(heading);
      if (heading !== _heading) {
         _heading = heading;
         document.dispatchEvent(new CustomEvent("bm_compass", { detail: { heading: _heading } }));
         console.log("COMPASS heading", _heading, e.alpha, e.beta, e.gamma);
      }
   }

   function _getHeading(errorCB) {
      //if (!_compassInitialized) _initializeCompass(errorCB);
      //if (_heading)
      //   document.dispatchEvent(new CustomEvent("bm_compass", { detail: { heading: _heading } }));
      return _heading;
   }

   function _compassDumper(ev) {
      let arr = ["COMPASS_DMP ev="];
      arr.push(ev.type);
      arr.push(', webkitCompassHeading=');
      arr.push(ev.webkitCompassHeading);
      arr.push(', alpha=');
      arr.push(ev.alpha);
      arr.push(', beta=');
      arr.push(ev.beta);
      arr.push(', gamma=');
      arr.push(ev.gamma);
      console.log(arr.join(''), JSON.stringify(ev));
   }

   let _compassInitialized;
   function _initializeCompass(errorCB) {
      if (_compassInitialized) return true;
      _compassInitialized = true;
      if (!errorCB) errorCB = _logErrorsCB;

      if (_compassPermission === 'denied') {
         errorCB('compass', 'Not allowed by permissions. Please check your settings.');
         return;
      }

      const evName = window.ondeviceorientationabsolute !== undefined ? "deviceorientationabsolute" : "deviceorientation";
      console.log("COMPASS event name: " + evName);

      if (DeviceOrientationEvent && typeof (DeviceOrientationEvent.requestPermission) === "function") {
         DeviceOrientationEvent.requestPermission()
            .then((response) => {
               if (response === "granted") {
                  window.addEventListener(evName, _compassHandler, true);
               } else {
                  errorCB('compass', 'Not allowed by permissions. Please check your settings.');
                  window.addEventListener(evName, _compassHandler, true);
               }
               _compassInitialized = true;
            })
            .catch(function (e) {
               console.log(e);
               errorCB('compass', 'Not supported. Error=' + e.message);
            });

      } else {
         //See https://stackoverflow.com/questions/49752559/absolute-device-orientation
         window.addEventListener(evName, _compassHandler, true);
         _compassInitialized = true;
      }
      return _compassInitialized;
   }



   return {
      getLocation: _getLocation,
      getCachedLocation: _getCachedLocation,
      initializeCompass: _initializeCompass,
      getHeading: _getHeading,
      alertingErrorCallback: _alertErrorsCB
   };
}

function createDummySensorsApi() {
   function _dummy() { };
   return {
      getLocation: _dummy,
      getCachedLocation: _dummy,
      initializeCompass: _dummy,
      getHeading: _dummy,
      alertingErrorCallback: _dummy
   };
}



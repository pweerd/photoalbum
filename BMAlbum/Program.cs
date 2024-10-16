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

using Bitmanager.Core;
using Bitmanager.Web;

namespace BMAlbum {
   public class Program {
      public static void Main (string[] args) {
         try {
            var builder = WebApplication.CreateBuilder (args);
            BMLogProvider.ReplaceLogProvider (builder);

            // Add services to the container.
            var services = builder.Services;
            services.AddControllersWithViews ();
            services.Add (new ServiceDescriptor (typeof (IServiceCollection), services));
            services.AddControllersWithViews ();
            var g = new WebGlobals (services);
            var settings = g.RegisterSettingsService (services, (fn, oldSettings) => new Settings (fn, oldSettings));
            g.RegisterRequestContextService (services);
            g.RegisterAuthenticationService (services, true);
            g.RegisterAccessControlService (services, false);

            var app = builder.Build ();

            g.Configure (app);
            g.SiteLog.Log ("ServiceProvider={0} ", app.Services.GetType ().FullName);
            g.SiteLog.Log ("Settings={0} ", settings);

            app.UseDeveloperExceptionPage ();
            g.RegisterExceptionHandler (app);
            g.RegisterRedirectHttps (app);
            g.RegisterAccessControlHandler (app);
            g.RegisterRequestLogging (app);

            app.UseStaticFiles ();
            app.UseRouting ();
            g.RegisterAuthenticationHandler (app);

            app.UseEndpoints (endpoints => {
               g.RegisterInternalHandlers (endpoints);
               g.RegisterRoutesFromSettings (endpoints, settings);
            });
            app.Run ();

         } catch (Exception e) {
            Console.Error.WriteLine ("Error during initialization: {0}", e.GetBestMessage ());
            Logs.ErrorLog.Log (e, "Error during initialization");
         }
      }
   }
}
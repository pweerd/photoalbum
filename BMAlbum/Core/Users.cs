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
using Bitmanager.Elastic;
using Bitmanager.Json;
using Bitmanager.Web;
using Bitmanager.Xml;
using Microsoft.AspNetCore.Mvc;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using static BMAlbum.AlbumFilter;

namespace BMAlbum {

   public enum _Access { Ok, NotExposed, MustAuthenticate };
   public class Users {
      public readonly User DefUser;
      private readonly Dictionary<string, User> users;

      public Users(XmlNode node) {
         DefUser = new User ();
         users = new Dictionary<string, User> ();
         if (node != null) {
            foreach (XmlNode sub in node.SelectNodes ("user")) {
               var user = new User (sub);
               users.Add (user.Id, user);
            }
         }

         if (users.ContainsKey (DefUser.Id)) DefUser = users[DefUser.Id];
         else users.Add(DefUser.Id, DefUser);
      }

      /// <summary>
      /// Returns the current user based on the ID
      /// If the ID is null, defUser is returned
      /// If the ID is found as a valid user, that user is returned
      /// Otherwise, a dummy user with a user-filter is returned
      /// </summary>
      public User GetUser (string id) {
         User u = DefUser;
         if (id != null) {
            if (users.TryGetValue (id, out u)) {
               if (DateTime.UtcNow > u.Expires) u = null;
            } else {
               u = new User (DefUser, id);
            };
         }
         return u;
      }

      public void Dump (Logger logger) {
         logger.Log("Dumping {0} users:", users.Count);
         foreach (var kvp in users) {
            var user = kvp.Value;
            logger.Log ("-- User {0}, name={1}, expose='{2}', skip_auth='{3}', expire={4}:", user.Id, user.Name, user.ExposeExpr, user.SkipAuthenticateExpr, user.Expires);
            var bq = kvp.Value.CreateFilter ();
            if (bq == null)
               logger.Log ("-- -- No filter");
            else 
               logger.Log ("-- -- {0}", bq.ToJsonString());
         }
      }

   }


   public class User {
      private static readonly Regex regexAny = new Regex (".", RegexOptions.Compiled | RegexOptions.CultureInvariant);
      private static readonly Regex regexIntern = new Regex ("^internal", RegexOptions.Compiled | RegexOptions.CultureInvariant);
      
      public readonly string Id;
      public readonly string Name;
      public readonly Regex SkipAuthenticateExpr;
      public readonly Regex ExposeExpr;
      public readonly AlbumFilter[] FilterSources;
      public readonly DateTime Expires;

      public User () {
         Name = "*";
         Id = "*";
         SkipAuthenticateExpr = regexAny;
         ExposeExpr = regexAny;
      }

      public User (User other, string name) {
         Name = name;
         Id = name;
         SkipAuthenticateExpr = other.SkipAuthenticateExpr;
         ExposeExpr = other.ExposeExpr;
         int N = 1 + (other.FilterSources==null ? 0 : other.FilterSources.Length);
         FilterSources = new AlbumFilter[N];
         if (N > 1) Array.Copy (other.FilterSources, FilterSources, N - 1);
         FilterSources[N - 1] = new AlbumTermFilter (_Clause.filter, "user", name);
      }

      public User (XmlNode node) {
         const RegexOptions OPTIONS = RegexOptions.Compiled | RegexOptions.CultureInvariant;
         Name = node.ReadStr ("@name");
         Id = node.ReadStr ("@id");
         string tmp;

         tmp = node.ReadStr ("@skip_authenticate", null);
         SkipAuthenticateExpr = tmp == null ? regexIntern : new Regex (tmp, OPTIONS);

         tmp = node.ReadStr ("@expose", null);
         ExposeExpr = tmp == null ? regexAny : new Regex (tmp, OPTIONS);

         Expires = node.ReadDate ("@expires", DateTime.MaxValue);

         var list = node.SelectNodes ("filter");
         if (list != null && list.Count>0) {
            FilterSources = new AlbumFilter[list.Count];
            for (int i = 0; i < list.Count; i++) {
               XmlNode sub = list[i];
               if (sub.HasAttribute ("value")) {
                  FilterSources[i] = new AlbumTermFilter (sub);
                  continue;
               }
               if (sub.HasAttribute ("prefix")) {
                  FilterSources[i] = new AlbumPrefixFilter (sub);
                  continue;
               }
               //if (sub.HasAttribute ("expr")) {
               //   FilterSources[i] = new AlbumRegexFilter (sub);
               //   continue;
               //}

               FilterSources[i] = new AlbumCustomFilter (sub);
            }
         }
      }

      public static _Access CheckAccess (User user, string remoteClass, bool isAuthenticated) {
         if (user == null) goto ACCESS_FORBIDDEN;
         if (user.ExposeExpr.IsMatch (remoteClass)) goto ACCESS_OK;
         if (isAuthenticated && user.ExposeExpr.IsMatch ("authenticated")) goto ACCESS_OK;

         //Check if we need to authenticate
         if (!isAuthenticated && !user.SkipAuthenticateExpr.IsMatch (remoteClass))
            return _Access.MustAuthenticate;

      ACCESS_FORBIDDEN:
         return _Access.NotExposed;

      ACCESS_OK:
         return _Access.Ok;
      }

      public ESQuery CreateFilter() {
         if (FilterSources == null) return null;
         if (FilterSources.Length == 1) return FilterSources[0].CreateFilter ();
         var bq = new ESBoolQuery (1);
         for (int i = 0; i < FilterSources.Length; i++) {
            var filterSrc = FilterSources[i];
            var q = filterSrc.CreateFilter ();
            switch (filterSrc.Clause) {
               case _Clause.should:
                  bq.ShouldClauses.Add (q);
                  continue;
               case _Clause.must:
                  bq.MustClauses.Add (q);
                  continue;
               case _Clause.must_not:
                  bq.NotClauses.Add (q);
                  continue;
               case _Clause.filter:
                  bq.FilterClauses.Add (q);
                  continue;
               default:
                  filterSrc.Clause.ThrowUnexpected (); break;
            }
         }
         return bq;
      }

      public override string ToString () {
         return Invariant.Format ("User[id={0}, n={1}]", Id, Name);
      }
   }


   public abstract class AlbumFilter {
      public enum _Clause { should, must, must_not, filter };
      public readonly _Clause Clause;
      public abstract ESQuery CreateFilter ();

      public AlbumFilter (XmlNode node) {
         Clause = node.ReadEnum ("@clause", _Clause.should);
      }
      public AlbumFilter (_Clause c) {
         Clause = c;
      }
   }
   public abstract class AlbumFieldFilter : AlbumFilter {
      public readonly string Field;
      public AlbumFieldFilter (XmlNode node) : base(node) {
         Field = node.ReadStr ("@field");
      }
      public AlbumFieldFilter (_Clause c, string field) : base (c) {
         Field = field;
      }
   }

   public class AlbumTermFilter : AlbumFieldFilter {
      public readonly string Value;
      public AlbumTermFilter (XmlNode node) : base (node) {
         Value = node.ReadStr ("@value");
      }
      public AlbumTermFilter (_Clause c, string field, string value) : base (c, field) {
         Value = value;
      }

      public override ESQuery CreateFilter () {
         return new ESMatchQuery (Field, Value).SetOperator(ESQueryOperator.and);
      }
   }

   public class AlbumPrefixFilter : AlbumFieldFilter {
      public readonly string Value;
      public AlbumPrefixFilter (XmlNode node) : base (node) {
         Value = node.ReadStr ("@prefix");
      }
      public override ESQuery CreateFilter () {
         return new ESPrefixQuery (Field, Value);
      }
   }

   public class AlbumCustomFilter : AlbumFilter {
      private readonly JsonObjectValue json;
      public AlbumCustomFilter (XmlNode node) : base (node) {
         json = JsonObjectValue.Parse (node.InnerText.Trim ());
      }
      public override ESQuery CreateFilter () {
         return new ESJsonQuery (json);
      }
   }
}

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

using Bitmanager.BoolParser;
using Bitmanager.Cache;
using Bitmanager.Core;
using Bitmanager.Elastic;
using Bitmanager.Json;
using Bitmanager.Query;
using Bitmanager.Web;
using Bitmanager.Xml;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace BMAlbum {

   public enum _Access { Ok, NotExposed, MustAuthenticate };

   public class Users {
      private readonly Func<User, bool> userExists;
      public readonly User DefUser;
      public readonly User Template;
      private readonly Dictionary<string, User> users;
      private readonly LRUCache dynamicUsers;

      public Users (XmlNode node, Func<User,bool> userChecker) {
         this.userExists = userChecker;
         dynamicUsers = new LRUCache (5);

         DefUser = createUser (node.SelectSingleNode ("default"));
         Template = createUser (node.SelectSingleNode ("template"));

         users = new Dictionary<string, User> ();
         if (node != null) {
            foreach (XmlNode sub in node.SelectNodes ("user")) {
               var user = new User (sub);
               users.Add (user.Id, user);
            }
         }
      }

      private static User createUser (XmlNode node) {
         return node==null ? null : new User (node);
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
            if (!users.TryGetValue (id, out u)) {
               if (Template == null) u = null;
               else {
                  u = (User)dynamicUsers.Get (id);
                  if (u == null) {
                     u = new User (Template, id);
                     if (userExists (u))
                        u = (User)dynamicUsers.GetOrAdd (id, u);
                     else
                        u = null;
                  }
               }
            }
         }
         if (u != null && DateTime.UtcNow > u.Expires) u = null;
         return u;
      }

      public void Dump (Logger logger) {
         logger.Log("Dumping {0} users:", users.Count);
         _dumpDefault (DefUser, logger, "default");
         _dumpDefault (Template, logger, "template");

         foreach (var kvp in users) {
            var user = kvp.Value;
            logger.Log ("-- User {0}, name={1}, expose='{2}', skip_auth='{3}', expire={4}:", user.Id, user.Name, user.ExposeExpr, user.SkipAuthenticateExpr, user.Expires);
            _dumpFilter (user, logger);
         }
      }

      private static void _dumpFilter (User user, Logger logger) {
         var bq = user.Filter;
         if (bq == null)
            logger.Log ("-- -- No filter");
         else
            logger.Log ("-- -- {0}", bq.ToJsonString ());
      }
      private static void _dumpDefault (User user, Logger logger, string type) {
         if (user == null)
            logger.Log ("-- {0}: null", type);
         else {
            logger.Log ("-- {0}: expose='{1}', skip_auth='{2}', expire={3}:", type, user.ExposeExpr, user.SkipAuthenticateExpr, user.Expires);
            _dumpFilter (user, logger);
         }
      }
   }


   public class User {
      private static readonly Regex regexAny = new Regex (".", RegexOptions.Compiled | RegexOptions.CultureInvariant);
      private static readonly Regex regexIntern = new Regex ("^internal", RegexOptions.Compiled | RegexOptions.CultureInvariant);
      
      public readonly string Id;
      public readonly string Name;
      public readonly string InitialSortMode;
      public readonly Regex SkipAuthenticateExpr;
      public readonly Regex ExposeExpr;
      public readonly ESQuery Filter;
      public readonly DateTime Expires;
      public readonly TriStateBool InitialPerAlbum;

      public User () {
         SkipAuthenticateExpr = regexIntern;
         ExposeExpr = regexIntern;
         Expires = DateTime.MaxValue;
      }

      public User (User other, string name) {
         Name = name;
         Id = name;
         SkipAuthenticateExpr = other.SkipAuthenticateExpr;
         ExposeExpr = other.ExposeExpr;
         Expires = other.Expires;
         Filter = ESBoolQuery.CreateFilteredQuery (other.Filter, new ESTermQuery ("user", name));
      }

      enum _Clause { should, must, must_not, filter };
      public User (XmlNode node) {
         const RegexOptions OPTIONS = RegexOptions.Compiled | RegexOptions.CultureInvariant;
         if (node.Name=="user") {
            Name = node.ReadStr ("@name");
            Id = node.ReadStr ("@id");
         }
         string tmp;

         InitialPerAlbum = node.ReadEnum ("@per_album", TriStateBool.Unspecified);
         InitialSortMode = node.ReadStr ("@sort", null);
         tmp = node.ReadStr ("@skip_authenticate", null);
         SkipAuthenticateExpr = tmp == null ? regexIntern : new Regex (tmp, OPTIONS);

         tmp = node.ReadStr ("@expose", null);
         ExposeExpr = tmp == null ? regexAny : new Regex (tmp, OPTIONS);

         Expires = node.ReadDate ("@expires", DateTime.MaxValue);

         var list = node.SelectNodes ("filter");
         if (list != null && list.Count>0) {
            ESQuery filter = null;
            switch (list.Count) {
               case 0: break;
               case 1:
                  filter = AlbumCollectionParser.ParseNode (list[0]);
                  break;
               default:
                  var bq = new ESBoolQuery (1);
                  filter = bq;
                  for (int i = 0; i < list.Count; i++) {
                     var q = AlbumCollectionParser.ParseNode (list[i]);
                     switch (node.ReadEnum ("@clause", _Clause.should)) {
                        case _Clause.should: bq.AddShould (q); break;
                        case _Clause.filter: bq.AddFilter (q); break;
                        case _Clause.must: bq.AddMust (q); break;
                        case _Clause.must_not: bq.AddNot (q); break;
                        default:
                           node.ReadEnum ("@clause", _Clause.should).ThrowUnexpected ();
                           break;
                     }
                  }
                  break;
            }
            Filter = filter;
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

      public override string ToString () {
         return Invariant.Format ("User[id={0}, n={1}]", Id, Name);
      }
   }



   public class AlbumCollectionParser {
      public static ESQuery ParseNode(XmlNode node) {
         if (node.HasAttribute ("value")) {
            return new ESMatchQuery (node.ReadStr("@field"), node.ReadStr ("@value")).SetOperator (ESQueryOperator.and);
         }
         if (node.HasAttribute ("prefix")) {
            return new ESPrefixQuery (node.ReadStr ("@field"), node.ReadStr ("@prefix"));
         }
         if (node.HasAttribute ("query")) {
            var qg = new FilterGenerator (node.ReadStr ("@query"), node.ReadBool ("@debug", false));
            //var qg = new QueryGenerator (searchSettings, indexInfo, node.ReadStr ("@query"));
            var ret = qg.GenerateQuery ();
            if (ret==null) throw new BMNodeException (node, "Filter did not result in a query.");
            return ret;
         }
         var json = JsonObjectValue.Parse (node.InnerText.Trim ());
         return new ESJsonQuery (json);
      }
   }

}

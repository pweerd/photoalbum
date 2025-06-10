using Bitmanager.Xml;
using SixLabors.Fonts.Tables.AdvancedTypographic;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Bitmanager.Json;
using System.Xml;

namespace AlbumImporter {
   public class Tagger {
      public readonly string User;
      public readonly string Field;
      public readonly string Tag;
      public readonly Regex Expr;

      public Tagger(XmlNode node) {
         User = node.ReadStr ("@user");
         Field = node.ReadStr ("@field");
         Tag = node.ReadStr ("@tag");
         Expr = new Regex (node.ReadStr ("@expr"), RegexOptions.CultureInvariant | RegexOptions.Compiled);
      }
   }

   public class Taggers {
      private readonly Tagger[] taggers;
      private Tagger[] curTaggers;
      private string curUser;
      public Taggers (XmlNode node) {
         if (node == null) return;
         var subNodes = node.SelectNodes ("tagger");
         if (subNodes.Count == 0) return;

         taggers = new Tagger[subNodes.Count];
         for (int i = 0; i < subNodes.Count; i++) {
            taggers[i] = new Tagger(subNodes[i]);
         }
         Array.Sort (taggers, (a,b)=>String.CompareOrdinal(a.Field, b.Field));
      }

      public int Count => taggers==null ? 0: taggers.Length;

      private void createTaggersForUser (string user) {
         var userTaggers = new List<Tagger> ();
         for (int i = 0; i < taggers.Length; i++) {
            if (taggers[i].User == "*" || string.Equals (taggers[i].User, user, StringComparison.InvariantCultureIgnoreCase))
               userTaggers.Add (taggers[i]);
         }
         curTaggers = userTaggers.Count == 0 ? null : userTaggers.ToArray ();
      }

      public void AddTags (string user, JsonObjectValue rec) {
         if (user != curUser) {
            curUser = user;
            createTaggersForUser (user);
         }
         if (curTaggers == null) return;

         HashSet<string> set = null;
         string field = null;
         string value = null;
         for (int i = 0; i < curTaggers.Length; i++) {
            var tagger = taggers[i];
            if (tagger.Field != field) {
               field = tagger.Field;
               value = rec.ReadStr (field, null);
               if (value == null) continue;
               value = value.ToLowerInvariant ();
            }
            if (value == null) continue;
            if (!tagger.Expr.IsMatch (value)) continue;

            if (set == null) set = new HashSet<string> (1);
            set.Add (tagger.Tag);
         }
         if (set != null)
            rec["tags"] = new JsonArrayValue (set);
      }
   }
}

using Bitmanager.Elastic;
using Bitmanager.Importer;
using Bitmanager.Xml;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;

namespace AlbumImporter.Captions {
   public class CaptionReplacers {
      private const RegexOptions OPTIONS = RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture;
      private readonly Regex[] expr;
      private readonly string[] repl;

      public CaptionReplacers(params string[] exprAndRepl) {
         if (exprAndRepl.Length % 2 != 0) throw new ArgumentException("exprAndRepl should contain an even #strings.");
         if (exprAndRepl.Length > 0) {
            expr = new Regex[exprAndRepl.Length / 2];
            repl = new string[exprAndRepl.Length / 2];
            for (int i = 0; i < expr.Length; i++) {
               repl[i] = exprAndRepl[2 * i + 1];
               expr[i] = new Regex(exprAndRepl[2 * i + 1], OPTIONS);
            }
         }
      }
      public CaptionReplacers(XmlNode node) {
         if (node == null) return;
         var list = node.SelectNodes("replace");
         int N = list.Count;
         if (N == 0) return;
         expr = new Regex[N];
         repl = new string[N];
         for (int i = 0; i < N; i++) {
            var replNode = list[i];
            repl[i] = replNode.ReadStr("@repl", string.Empty);
            expr[i] = new Regex(replNode.ReadStr("@expr"), OPTIONS);
         }
      }


      public string Replace(string txt) {
         if (string.IsNullOrEmpty(txt) || expr ==null) return txt;
         for (int i = 0; i < expr.Length; i++) {
            txt = expr[i].Replace(txt, repl[i]).Trim();
            if (txt.Length == 0) return txt;
         }
         return (char.IsUpper(txt[0])) ? txt : char.ToUpperInvariant(txt[0]) + txt.Substring(1);
      }
   }
}

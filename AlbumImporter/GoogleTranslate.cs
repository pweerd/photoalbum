using Bitmanager.Core;
using Bitmanager.Http;
using Bitmanager.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace AlbumImporter {
   // https://github.com/lushan88a/google_trans_new
   // https://kovatch.medium.com/deciphering-google-batchexecute-74991e4e446c
   public class GoogleTranslator {
      private readonly CancellationToken cancelToken;
      private readonly HttpSession http;
      private readonly string url;
      private static readonly MediaTypeHeaderValue mediaType = new MediaTypeHeaderValue("application/x-www-form-urlencoded", "utf-8");


      public GoogleTranslator(string urlSuffix = "com", int timeout = 10000)
         : this(urlSuffix, timeout, CancellationToken.None) {
      }
      public GoogleTranslator(CancellationToken ct)
         : this("com", 10000, ct) {
      }
      public GoogleTranslator(string urlSuffix, int timeout, CancellationToken ct) {
         cancelToken = ct;
         http = new HttpSession();
         http.TimeoutMs = timeout;
         string url = "https://translate.google." + urlSuffix + "/";
         http.SetDefaultHeader("referer", url);
         this.url = url + "_/TranslateWebserverUi/data/batchexecute";
         http.SetUserAgent("Mozilla/5.0 (Windows NT 10.0; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/47.0.2526.106 Safari/537.36");
      }

      // https://kovatch.medium.com/deciphering-google-batchexecute-74991e4e446c
      private static string createRPC(string text, string tgtLang, string srcLang) {
         text = text.Trim();
         string escaped = Encoders.EscapeJavascript(text);
         if (escaped.Length != text.Length) escaped = Encoders.EscapeJavascript(escaped);
         string rpc = Invariant.Format("[[[\"MkEWBc\",\"[[\\\"{0}\\\",\\\"{1}\\\",\\\"{2}\\\",true],[1]]\",null,\"generic\"]]]",
         escaped,
         srcLang,
         tgtLang);

         return "f.req=" + HttpUtility.UrlEncode(rpc) + "&";
      }

      /// <summary>
      ///     Translates the specified text.
      /// </summary>
      /// <returns>translated text as a string</returns>
      public string Translate(string text, string tgtLang = "auto", string srcLang = "auto") {
         if (string.IsNullOrEmpty(text)) return text;
         if (text.Length >= 5000) throw new BMException("Too many characters for translation: {0}. Max 5000 characters allowed", text.Length);

         string rpc = createRPC(text, tgtLang, srcLang);

         var resp = http.Post(url, HttpPayload.Create(rpc).SetMediaType(mediaType), cancelToken);
         resp.ThrowIfError();
         try {
            var strm = resp.Content;
            strm.Position = 0;
            string firstBytes = Encoding.Latin1.GetString(strm.GetBuffer(), 0, 32);
            strm.Position = firstBytes.IndexOf("[[");
            var json = (JsonArrayValue)JsonValue.Load(strm);
            json = (JsonArrayValue)json[0];
            //Logs.DebugLog.Log("Inner Json:\n{0}", json);

            json = (JsonArrayValue)JsonValue.Parse(json[2].AsString());
            Logs.DebugLog.Log("Parsed inner1 Json:\n{0}", json);


            json = (JsonArrayValue)json[1];
            json = (JsonArrayValue)json[0];
            json = (JsonArrayValue)json[0];
            json = (JsonArrayValue)json[5];
            Logs.DebugLog.Log("Parsed inner2 Json:\n{0}", json);

            string ret = null;
            for (int i = 0; i < json.Count; i++) {
               var tmp = (JsonArrayValue)json[i];
               string tr = removeUnwantedChars(tmp[0].AsString());

               if (tr.Length == 0) continue;
               if (ret == null) ret = tr;
               else if (char.IsWhiteSpace(ret[^1]) || char.IsWhiteSpace(tr[0])) ret += tr;
               else ret = ret + ' ' + tr;

            }
            return ret;
         }
         catch {
            Logs.ErrorLog.Log(_LogType.ltInfo, "Problem in Google translate. Returned buffer:\n{0}", resp.StrValue);
            throw;
         }
      }

      private static string removeUnwantedChars(string s) {
         int i=0;
         for (; i < s.Length; i++) {
            if (0x200B == s[i]) goto REMOVE;
         }
         return s;

      REMOVE:
         var sb = s.ToCharArray();
         bool removed_200B = false;
         int j=i;
         for (; i < sb.Length; i++) {
            switch (sb[i]) {
               case '\u200B':
                  removed_200B = true;
                  continue;
               default:
                  if (removed_200B && sb[i] != ' ') {
                     if (j > 0 && sb[j - 1] != ' ') sb[j++] = ' ';
                  }
                  sb[j++] = sb[i];
                  removed_200B = false;
                  continue;
            }
         }
         return new string(sb, 0, j);
      }

   }
}

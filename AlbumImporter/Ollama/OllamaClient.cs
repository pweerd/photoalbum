using Bitmanager.Http;
using Bitmanager.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AlbumImporter {
   public class OllamaClient {
      string url = "http://localhost:11434/api/generate";
      private readonly HttpSession http;
      public readonly JsonObjectValue Template;
      public readonly string Url;
      private static readonly JsonObjectValue defTemplate;
      private readonly CancellationToken cancelToken;

      static OllamaClient() { 
         defTemplate = new JsonObjectValue();
         defTemplate["model"] = "llava";
         defTemplate["prompt"] = "Describe the image in 1 sentence";
         defTemplate["images"] = new JsonArrayValue((JsonValue)"");
         defTemplate["stream"] = false;
         JsonObjectValue options;
         defTemplate["options"] = options = new JsonObjectValue("seed", 42);
         //"num_ctx", 0, 
         options["keep_alive"] = "10m";
      }

      public OllamaClient(CancellationToken ct) {
         http = new HttpSession();
         Url = "http://localhost:11434/api/generate";
         Template = defTemplate;
         cancelToken = ct;
      }
      public OllamaClient(string url, JsonObjectValue template, CancellationToken ct) {
         http = new HttpSession();
         Url = url;
         Template = template ?? defTemplate;
         var arr = Template.ReadArr("images", null);
         if (arr == null) Template.Add("images", arr = new JsonArrayValue());
         if (arr.Count == 0) arr.Add(JsonNullValue.Instance);
         cancelToken = ct;
      }

      public JsonObjectValue Post(JsonObjectValue v) {
         var payload = HttpPayload.Create (v);
         var resp = http.Post (url, payload, cancelToken);
         resp.ThrowIfError();
         return resp.Json;
      }
      public JsonObjectValue Post(string img) {
         var json = Template;
         var imgs = json.ReadArr("images");
         imgs[0] = new JsonBinaryFileValue(img);
         return Post(json);
      }
      public JsonObjectValue Post(JsonValue img) {
         var json = Template;
         var imgs = json.ReadArr("images");
         imgs[0] = img;
         return Post(json);
      }

      public string PostGetResponse(JsonObjectValue v) {
         return Post(v).ReadStr("response");
      }
      public string PostGetResponse(string img) {
         var json = Template;
         var imgs = json.ReadArr("images");
         imgs[0] = new JsonBinaryFileValue(img);
         return PostGetResponse(json);
      }
      public string PostGetResponse(JsonValue img) {
         var json = Template;
         var imgs = json.ReadArr("images");
         imgs[0] = img;
         return PostGetResponse(json);
      }
   }
}

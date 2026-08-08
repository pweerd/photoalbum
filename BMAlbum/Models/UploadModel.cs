using Bitmanager.Web;
using Microsoft.AspNetCore.Html;
using System.Text;

namespace BMAlbum.Models {
   public class UploadModel {
      public readonly ClientState State;
      public readonly RequestContext RequestCtx;
      public readonly string ActivateCommand;
      public readonly string ActivateAction;
      public readonly string ActivateState;
      public readonly string CommandUrl;
      public readonly string HiddenClass;
      public readonly bool Disabled;

      public UploadModel (BaseController c, ClientState state) {
         RequestCtx = c.RequestCtx;
         HiddenClass = (RequestCtx.IsInternalIp) ? string.Empty : "hidden";
         State = state;
         var uploadSettings = state.Settings.UploadSettings;
         if (uploadSettings == null) {
            Disabled = true;

         } else if (uploadSettings.Active) {
            ActivateState = "Active";
            ActivateCommand = "deactivate";
            ActivateAction = "Deactivate";
         } else {
            ActivateState = "NotActive";
            ActivateCommand = "activate";
            ActivateAction = "Activate";
         }
         var sb = new StringBuilder();
         sb.Append (WebGlobals.Instance.BasePath).Append ("upload/adminexec?");
         state.ExportUrlParameters (sb);
         CommandUrl = sb.ToString (); 
      }

      public HtmlString GetStateAsHtmlString () {
         return State.ToJsonAsHtmlString ();
      }

   }
}

using Nop.Web.Framework.Mvc.ModelBinding;
using Nop.Web.Framework.Models;

namespace Nop.Plugin.Payments.AmeriaVPos.Models
{
    public record ConfigurationModel : BaseNopModel
    {
        public int ActiveStoreScopeConfiguration { get; set; }

        [NopResourceDisplayName("Plugins.Payments.AmeriaVPos.Fields.UseSandbox")]
        public bool UseSandbox { get; set; }
        public bool UseSandbox_OverrideForStore { get; set; }

        [NopResourceDisplayName("Plugins.Payments.AmeriaVPos.Fields.ClientId")]
        public string ClientId { get; set; }
        public bool ClientId_OverrideForStore { get; set; }

        [NopResourceDisplayName("Plugins.Payments.AmeriaVPos.Fields.Username")]
        public string Username { get; set; }
        public bool Username_OverrideForStore { get; set; }

        [NopResourceDisplayName("Plugins.Payments.AmeriaVPos.Fields.Password")]
        public string Password { get; set; }
        public bool Password_OverrideForStore { get; set; }

        [NopResourceDisplayName("Plugins.Payments.AmeriaVPos.Fields.ApiBaseUrl")]
        public string ApiBaseUrl { get; set; }
        public bool ApiBaseUrl_OverrideForStore { get; set; }

        [NopResourceDisplayName("Plugins.Payments.AmeriaVPos.Fields.PayBaseUrl")]
        public string PayBaseUrl { get; set; }
        public bool PayBaseUrl_OverrideForStore { get; set; }
    }
}

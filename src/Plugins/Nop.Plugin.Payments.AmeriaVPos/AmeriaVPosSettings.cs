using Nop.Core.Configuration;

namespace Nop.Plugin.Payments.AmeriaVPos
{
    public class AmeriaVPosSettings : ISettings
    {
        /// <summary>
        /// Gets or sets a value indicating whether to use the AmeriaBank sandbox environment
        /// </summary>
        public bool UseSandbox { get; set; }

        /// <summary>
        /// Gets or sets the merchant Client ID issued by AmeriaBank
        /// </summary>
        public string ClientId { get; set; }

        /// <summary>
        /// Gets or sets the merchant username issued by AmeriaBank
        /// </summary>
        public string Username { get; set; }

        /// <summary>
        /// Gets or sets the merchant password issued by AmeriaBank
        /// </summary>
        public string Password { get; set; }

        /// <summary>
        /// Gets or sets the base URL for the VPOS REST API (e.g. https://servicestest.ameriabank.am/VPOS
        /// for sandbox). AmeriaBank has not yet issued a production hostname - this must be set to the
        /// real one before UseSandbox is turned off.
        /// </summary>
        public string ApiBaseUrl { get; set; }

        /// <summary>
        /// Gets or sets the base URL for the hosted pay page the customer is redirected to
        /// </summary>
        public string PayBaseUrl { get; set; }

        /// <summary>
        /// Gets or sets the number of minutes an initiated payment attempt is left
        /// unresolved before the reconciliation task treats it as abandoned
        /// </summary>
        public int AbandonedAttemptTimeoutMinutes { get; set; } = 25;
    }
}

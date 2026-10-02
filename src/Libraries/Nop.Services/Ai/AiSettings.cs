using Nop.Core.Configuration;

namespace Nop.Services.Ai
{
    /// <summary>
    /// Per-use-case KubeAI model selection, admin-configurable (Setting table, per-store override)
    /// via the usual nopCommerce settings mechanism - every property defaults to
    /// <see cref="KubeAiChatClient.DefaultModel"/> until an admin overrides it.
    /// </summary>
    public class AiSettings : ISettings
    {
        /// <summary>Model used to generate a support case's subject line from its description.</summary>
        public string SupportSubjectModel { get; set; } = KubeAiChatClient.DefaultModel;

        /// <summary>Model used for RemindMe's meal recommendation.</summary>
        public string RemindMeModel { get; set; } = KubeAiChatClient.DefaultModel;

        /// <summary>Model used by the Insights BI agent (chat + background agents).</summary>
        public string InsightsModel { get; set; } = KubeAiChatClient.DefaultModel;

        /// <summary>Model used by the storefront AI Assistant chat.</summary>
        public string AiChatModel { get; set; } = KubeAiChatClient.DefaultModel;
    }
}

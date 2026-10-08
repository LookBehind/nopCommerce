using System.Collections.Generic;
using System.Threading.Tasks;
using Nop.Plugin.Company.Support.Domain;
using Nop.Services.Messages;

namespace Nop.Plugin.Company.Support.Services
{
    /// <summary>
    /// Builds message tokens for a SupportCase. Lives in this plugin (not core's
    /// MessageTokenProvider) because SupportCase is a plugin-owned entity that core
    /// can't reference.
    /// </summary>
    public interface ISupportCaseTokenProvider
    {
        /// <summary>
        /// Adds SupportCase.* tokens, including SupportCase.SelfUrl (the admin edit page).
        /// </summary>
        /// <param name="tokens">List of already added tokens</param>
        /// <param name="supportCase">Support case</param>
        Task AddSupportCaseTokensAsync(IList<Token> tokens, SupportCase supportCase);
    }
}

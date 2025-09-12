using System.Threading.Tasks;
using Nop.Services.Plugins;

namespace Nop.Plugin.Company.Company
{
    /// <summary>
    /// Company plugin class
    /// </summary>
    public class CompanyPlugin : BasePlugin
    {
        #region Methods

        /// <summary>
        /// Install plugin
        /// </summary>
        /// <returns>A task that represents the asynchronous operation</returns>
        public override async Task InstallAsync()
        {
            await base.InstallAsync();
        }

        /// <summary>
        /// Uninstall plugin
        /// </summary>
        /// <returns>A task that represents the asynchronous operation</returns>
        public override async Task UninstallAsync()
        {
            await base.UninstallAsync();
        }

        #endregion
    }
}

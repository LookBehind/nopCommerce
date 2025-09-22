using System.Collections.Generic;
using System.Threading.Tasks;
using Nop.Core;
using Nop.Services.Cms;
using Nop.Services.Configuration;
using Nop.Services.Plugins;
using Nop.Web.Framework.Infrastructure;

namespace Nop.Plugin.Company.Company
{
    /// <summary>
    /// Company plugin class with delivery date picker widget functionality
    /// </summary>
    public class CompanyPlugin : BasePlugin, IWidgetPlugin
    {
        #region Fields

        private readonly IWebHelper _webHelper;
        private readonly ISettingService _settingService;

        #endregion

        #region Ctor

        public CompanyPlugin(
            IWebHelper webHelper,
            ISettingService settingService)
        {
            _webHelper = webHelper;
            _settingService = settingService;
        }

        #endregion

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

        /// <summary>
        /// Gets widget zones where this widget should be rendered
        /// </summary>
        /// <returns>Widget zones</returns>
        public Task<IList<string>> GetWidgetZonesAsync()
        {
            return Task.FromResult<IList<string>>(new List<string>
            {
                PublicWidgetZones.HeaderSelectors,
                PublicWidgetZones.Footer
            });
        }

        /// <summary>
        /// Gets a name of a view component for displaying widget
        /// </summary>
        /// <param name="widgetZone">Name of the widget zone</param>
        /// <returns>View component name</returns>
        public string GetWidgetViewComponentName(string widgetZone)
        {
            if (widgetZone == PublicWidgetZones.HeaderSelectors)
                return "GlobalDeliveryDatePicker";
            
            if (widgetZone == PublicWidgetZones.Footer)
                return "GlobalDeliveryTimeValidation";
                
            return "GlobalDeliveryDatePicker";
        }

        /// <summary>
        /// Gets a configuration page URL
        /// </summary>
        public override string GetConfigurationPageUrl()
        {
            return null; // No configuration needed for now
        }

        /// <summary>
        /// Gets a value indicating whether to hide this plugin on the widget list page in the admin area
        /// </summary>
        public bool HideInWidgetList => false;

        #endregion
    }
}

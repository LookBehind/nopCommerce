using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Routing;
using Nop.Core;
using Nop.Core.Domain.Security;
using Nop.Data;
using Nop.Plugin.Company.Insights.Security;
using Nop.Services.Plugins;
using Nop.Services.Security;
using Nop.Web.Framework.Menu;

namespace Nop.Plugin.Company.Insights
{
    /// <summary>
    /// Company Insights plugin — AI-chat BI workspace for backoffice users.
    /// Registers the <c>AccessInsights</c> permission, adds a gated admin-sidebar entry,
    /// and exposes the admin bootstrap route that hosts the SPA.
    /// </summary>
    public class InsightsPlugin : BasePlugin, IAdminMenuPlugin
    {
        #region Fields

        private readonly IWebHelper _webHelper;
        private readonly IPermissionService _permissionService;
        private readonly IRepository<PermissionRecord> _permissionRecordRepository;

        #endregion

        #region Ctor

        public InsightsPlugin(
            IWebHelper webHelper,
            IPermissionService permissionService,
            IRepository<PermissionRecord> permissionRecordRepository)
        {
            _webHelper = webHelper;
            _permissionService = permissionService;
            _permissionRecordRepository = permissionRecordRepository;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Configuration page = the Insights workspace itself.
        /// </summary>
        public override string GetConfigurationPageUrl()
        {
            return $"{_webHelper.GetStoreLocation()}Admin/Insights";
        }

        /// <summary>
        /// Install: register the AccessInsights permission (+ default Administrators mapping).
        /// </summary>
        public override async Task InstallAsync()
        {
            await _permissionService.InstallPermissionsAsync(new InsightsPermissionProvider());
            await base.InstallAsync();
        }

        /// <summary>
        /// Uninstall: best-effort removal of the permission and its role mappings.
        /// </summary>
        public override async Task UninstallAsync()
        {
            var systemName = InsightsPermissionProvider.AccessInsights.SystemName;
            var permission = (await _permissionService.GetAllPermissionRecordsAsync())
                .FirstOrDefault(p => p.SystemName == systemName);

            if (permission != null)
            {
                foreach (var mapping in await _permissionService.GetMappingByPermissionRecordIdAsync(permission.Id))
                    await _permissionService.DeletePermissionRecordCustomerRoleMappingAsync(permission.Id, mapping.CustomerRoleId);

                await _permissionRecordRepository.DeleteAsync(permission);
            }

            await base.UninstallAsync();
        }

        /// <summary>
        /// Adds a top-level "Insights" item to the admin sidebar (right after Dashboard),
        /// shown only to users who hold the <c>AccessInsights</c> permission.
        /// </summary>
        public async Task ManageSiteMapAsync(SiteMapNode rootNode)
        {
            if (!await _permissionService.AuthorizeAsync(InsightsPermissionProvider.AccessInsights))
                return;

            var node = new SiteMapNode
            {
                SystemName = "Company.Insights",
                Title = "Insights",
                Url = $"{_webHelper.GetStoreLocation()}Admin/Insights",
                IconClass = "far fa-chart-bar",
                Visible = true,
                RouteValues = new RouteValueDictionary { { "area", "Admin" } }
            };

            // Place just under Dashboard when present, otherwise at the top.
            var dashboardIndex = rootNode.ChildNodes
                .ToList()
                .FindIndex(n => n.SystemName == "Dashboard");
            if (dashboardIndex >= 0)
                rootNode.ChildNodes.Insert(dashboardIndex + 1, node);
            else
                rootNode.ChildNodes.Insert(0, node);
        }

        #endregion
    }
}

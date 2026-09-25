using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Routing;
using Nop.Core;
using Nop.Core.Domain.Security;
using Nop.Data;
using Nop.Plugin.Company.Support.Security;
using Nop.Services.Plugins;
using Nop.Services.Security;
using Nop.Web.Framework.Menu;

namespace Nop.Plugin.Company.Support
{
    /// <summary>
    /// Company Support plugin - customer support case inquiries (mobile-submitted) with an
    /// admin queue for staff to self-assign and track status.
    /// </summary>
    public class SupportPlugin : BasePlugin, IAdminMenuPlugin
    {
        private readonly IWebHelper _webHelper;
        private readonly IPermissionService _permissionService;
        private readonly IRepository<PermissionRecord> _permissionRecordRepository;

        public SupportPlugin(
            IWebHelper webHelper,
            IPermissionService permissionService,
            IRepository<PermissionRecord> permissionRecordRepository)
        {
            _webHelper = webHelper;
            _permissionService = permissionService;
            _permissionRecordRepository = permissionRecordRepository;
        }

        public override string GetConfigurationPageUrl()
        {
            return $"{_webHelper.GetStoreLocation()}Admin/SupportCase/List";
        }

        public override async Task InstallAsync()
        {
            await _permissionService.InstallPermissionsAsync(new SupportPermissionProvider());
            await base.InstallAsync();
        }

        public override async Task UninstallAsync()
        {
            var systemName = SupportPermissionProvider.ManageSupportCases.SystemName;
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
        /// Adds a "Support Cases" item to the admin sidebar (right after Dashboard), shown
        /// only to users who hold the ManageSupportCases permission.
        /// </summary>
        public async Task ManageSiteMapAsync(SiteMapNode rootNode)
        {
            if (!await _permissionService.AuthorizeAsync(SupportPermissionProvider.ManageSupportCases))
                return;

            var node = new SiteMapNode
            {
                SystemName = "Company.Support.Cases",
                Title = "Support Cases",
                Url = $"{_webHelper.GetStoreLocation()}Admin/SupportCase/List",
                IconClass = "far fa-life-ring",
                Visible = true,
                RouteValues = new RouteValueDictionary { { "area", "Admin" } }
            };

            var dashboardIndex = rootNode.ChildNodes
                .ToList()
                .FindIndex(n => n.SystemName == "Dashboard");
            if (dashboardIndex >= 0)
                rootNode.ChildNodes.Insert(dashboardIndex + 1, node);
            else
                rootNode.ChildNodes.Insert(0, node);
        }
    }
}

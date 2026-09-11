using System.Collections.Generic;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Security;
using Nop.Services.Security;

namespace Nop.Plugin.Company.Insights.Security
{
    /// <summary>
    /// Permission provider for the Company Insights plugin.
    /// Installed from <see cref="InsightsPlugin.InstallAsync"/> via
    /// <see cref="IPermissionService.InstallPermissionsAsync"/>, so it never touches the
    /// shared <c>StandardPermissionProvider</c>.
    /// </summary>
    public class InsightsPermissionProvider : IPermissionProvider
    {
        /// <summary>
        /// Access to the AI Insights backoffice workspace.
        /// </summary>
        public static readonly PermissionRecord AccessInsights = new PermissionRecord
        {
            Name = "Admin area. Access Company Insights (AI analytics)",
            SystemName = "AccessInsights",
            Category = "Company"
        };

        /// <summary>
        /// Get all permissions this provider manages.
        /// </summary>
        public IEnumerable<PermissionRecord> GetPermissions()
        {
            return new[] { AccessInsights };
        }

        /// <summary>
        /// Default role -> permission mapping applied on install.
        /// Administrators get access by default; other roles are granted in the admin ACL UI.
        /// </summary>
        public HashSet<(string systemRoleName, PermissionRecord[] permissions)> GetDefaultPermissions()
        {
            return new HashSet<(string, PermissionRecord[])>
            {
                (NopCustomerDefaults.AdministratorsRoleName, new[] { AccessInsights })
            };
        }
    }
}

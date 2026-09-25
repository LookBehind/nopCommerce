using System.Collections.Generic;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Security;
using Nop.Services.Security;

namespace Nop.Plugin.Company.Support.Security
{
    /// <summary>
    /// Permission provider for the Company Support plugin. Installed from
    /// SupportPlugin.InstallAsync via IPermissionService.InstallPermissionsAsync, so it never
    /// touches the shared StandardPermissionProvider.
    /// </summary>
    public class SupportPermissionProvider : IPermissionProvider
    {
        public static readonly PermissionRecord ManageSupportCases = new PermissionRecord
        {
            Name = "Admin area. Manage Support Cases",
            SystemName = "ManageSupportCases",
            Category = "Company"
        };

        public IEnumerable<PermissionRecord> GetPermissions()
        {
            return new[] { ManageSupportCases };
        }

        public HashSet<(string systemRoleName, PermissionRecord[] permissions)> GetDefaultPermissions()
        {
            return new HashSet<(string, PermissionRecord[])>
            {
                (NopCustomerDefaults.AdministratorsRoleName, new[] { ManageSupportCases })
            };
        }
    }
}

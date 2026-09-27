using System.Threading.Tasks;
using Nop.Core.Domain.Customers;
using Nop.Plugin.Company.AiChat.Security;
using Nop.Services.Customers;
using Nop.Services.Plugins;

namespace Nop.Plugin.Company.AiChat
{
    /// <summary>
    /// Company AI Chat Assistant plugin - mobile-v2's "AI Assistant" chat screen, gated behind
    /// the AiChatBetaTester customer role.
    /// </summary>
    public class AiChatPlugin : BasePlugin
    {
        private readonly ICustomerService _customerService;

        public AiChatPlugin(ICustomerService customerService)
        {
            _customerService = customerService;
        }

        public override async Task InstallAsync()
        {
            var existingRole = await _customerService.GetCustomerRoleBySystemNameAsync(AiChatRoles.BetaTesterSystemName);
            if (existingRole == null)
            {
                await _customerService.InsertCustomerRoleAsync(new CustomerRole
                {
                    Name = AiChatRoles.BetaTesterName,
                    SystemName = AiChatRoles.BetaTesterSystemName,
                    Active = true,
                    IsSystemRole = false
                });
            }

            await base.InstallAsync();
        }

        public override async Task UninstallAsync()
        {
            var role = await _customerService.GetCustomerRoleBySystemNameAsync(AiChatRoles.BetaTesterSystemName);
            if (role != null)
                await _customerService.DeleteCustomerRoleAsync(role);

            await base.UninstallAsync();
        }
    }
}

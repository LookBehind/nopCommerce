using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Nop.Plugin.Notifications.Manager.Services;

/// <summary>
/// Registered when the Telegram user-account MTProto credentials aren't configured for this tenant.
/// Every real caller must check <see cref="IsConfigured"/> first (the admin config page does,
/// <see cref="EventConsumer.VendorTelegramGroupConsumer"/> does via
/// <see cref="NotificationManagerSettings.TelegramGroupAutoCreationEnabled"/>) - every method here
/// throws loudly, as a safety net for a call site that forgot to check, not an expected path.
/// </summary>
public class NullTelegramGroupProvisioningService : ITelegramGroupProvisioningService
{
    private const string ERROR_MESSAGE =
        "Telegram group auto-creation was invoked but the MTProto user-account client isn't configured for this tenant.";

    public bool IsConfigured => false;

    public Task ProvisionVendorGroupAsync(int vendorId, int storeId) =>
        throw new NotImplementedException(ERROR_MESSAGE);

    public Task<IReadOnlyList<AutoInviteEntry>> GetAutoInviteEntriesAsync(int storeId) =>
        throw new NotImplementedException(ERROR_MESSAGE);

    public Task<AutoInviteCandidate> ResolveAutoInviteCandidateAsync(string identifier) =>
        throw new NotImplementedException(ERROR_MESSAGE);

    public Task<AutoInviteCandidate> AddAutoInviteUserAsync(int storeId, string identifier) =>
        throw new NotImplementedException(ERROR_MESSAGE);

    public Task RemoveAutoInviteUserAsync(int storeId, string identifier) =>
        throw new NotImplementedException(ERROR_MESSAGE);

    public Task<int> GetGroupCountAsync(int storeId) =>
        throw new NotImplementedException(ERROR_MESSAGE);

    public Task<IReadOnlyList<AutoInviteCandidate>> GetTelegramContactsAsync() =>
        throw new NotImplementedException(ERROR_MESSAGE);

    public Task<IReadOnlyList<VendorChatFixPreview>> GetVendorChatFixPreviewsAsync(int storeId) =>
        throw new NotImplementedException(ERROR_MESSAGE);

    public Task FixVendorChatTopicsAsync(int vendorId, int storeId) =>
        throw new NotImplementedException(ERROR_MESSAGE);

    public Task FixAllVendorChatTopicsAsync(int storeId) =>
        throw new NotImplementedException(ERROR_MESSAGE);

    public Task<IReadOnlyList<AutoInviteMembershipStatus>> GetAutoInviteMembershipStatusAsync(int storeId) =>
        throw new NotImplementedException(ERROR_MESSAGE);

    public Task RefreshAutoInviteMembershipStatusAsync(int storeId) =>
        throw new NotImplementedException(ERROR_MESSAGE);

    public Task FixAutoInviteUserMembershipAsync(int storeId, string identifier) =>
        throw new NotImplementedException(ERROR_MESSAGE);
}

using System;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Nop.Core;
using Nop.Core.Configuration;
using Nop.Core.Domain.Vendors;
using Nop.Core.Events;
using Nop.Plugin.Notifications.Manager.Services;
using Nop.Services.Configuration;
using Nop.Services.Events;
using Nop.Services.Logging;

namespace Nop.Plugin.Notifications.Manager.EventConsumer;

/// <summary>
/// Fires whenever a Vendor is inserted (e.g. via the admin "Create Vendor" form) and, when the
/// feature is enabled, enqueues a Hangfire fire-and-forget job to auto-create a Telegram group for
/// it - replacing the fully-manual onboarding flow (human creates group, adds bot,
/// <c>/associate_with_vendor</c>) with an automatic one. See docs/plans/2026-07-31-telegram-vendor-group-auto-open.md.
///
/// Auto-discovered and registered via the IConsumer&lt;T&gt; convention (no explicit DI entry),
/// same as <see cref="Nop.Plugin.Company.Company.Infrastructure.VendorPictureSquareConsumer"/>.
///
/// Enqueues rather than awaiting the MTProto work inline, so the admin's "Create Vendor" request
/// isn't blocked on Telegram round-trips, and Hangfire's retry policy (see
/// <see cref="TelegramGroupProvisioningService.ProvisionVendorGroupAsync"/>'s [AutomaticRetry])
/// covers transient failures for free.
/// </summary>
public class VendorTelegramGroupConsumer : IConsumer<EntityInsertedEvent<Vendor>>
{
    private readonly IStoreContext _storeContext;
    private readonly ISettingService _settingService;
    private readonly IServiceProvider _serviceProvider;
    private readonly AppSettings _appSettings;
    private readonly ILogger _logger;

    public VendorTelegramGroupConsumer(
        IStoreContext storeContext,
        ISettingService settingService,
        IServiceProvider serviceProvider,
        AppSettings appSettings,
        ILogger logger)
    {
        _storeContext = storeContext;
        _settingService = settingService;
        _serviceProvider = serviceProvider;
        _appSettings = appSettings;
        _logger = logger;
    }

    public async Task HandleEventAsync(EntityInsertedEvent<Vendor> eventMessage)
    {
        var vendor = eventMessage?.Entity;
        if (vendor == null)
            return;

        try
        {
            if (!_appSettings.ExtendedAuthSettings.TelegramBotEnabled)
                return;

            var notificationManagerSettings = await _settingService.LoadSettingAsync<NotificationManagerSettings>();
            if (!notificationManagerSettings.TelegramGroupAutoCreationEnabled)
                return;

            var store = await _storeContext.GetCurrentStoreAsync();

            // Resolved lazily (not constructor-injected) so a tenant without Hangfire's client wired up
            // yet can't have this consumer break every vendor creation - only enabling the feature can.
            var backgroundJobClient = _serviceProvider.GetRequiredService<IBackgroundJobClient>();
            backgroundJobClient.Enqueue<ITelegramGroupProvisioningService>(
                s => s.ProvisionVendorGroupAsync(vendor.Id, store.Id));
        }
        catch (Exception e)
        {
            await _logger.ErrorAsync($"Error enqueueing Telegram group auto-creation for vendor '{vendor.Name}'", e);
        }
    }
}

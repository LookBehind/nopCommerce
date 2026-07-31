using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nop.Core.Domain.Vendors;
using Nop.Services.Common;
using Nop.Services.Logging;
using Nop.Services.Stores;
using Nop.Services.Vendors;

namespace Nop.Plugin.Notifications.Manager.Services;

public class VendorTelegramChatCache : IVendorTelegramChatCache
{
    public const string VENDOR_TELEGRAM_CHANNEL_KEY = nameof(VENDOR_TELEGRAM_CHANNEL_KEY);

    private static readonly SemaphoreSlim _reloadSemaphore = new(1, 1);
    private static Dictionary<TelegramChatId, VendorAssociation> _chatIdToVendor;

    private readonly IVendorService _vendorService;
    private readonly IStoreService _storeService;
    private readonly IGenericAttributeService _genericAttributeService;
    private readonly ILogger _logger;

    public VendorTelegramChatCache(
        IVendorService vendorService,
        IStoreService storeService,
        IGenericAttributeService genericAttributeService,
        ILogger logger)
    {
        _vendorService = vendorService;
        _storeService = storeService;
        _genericAttributeService = genericAttributeService;
        _logger = logger;
    }

    public IReadOnlyDictionary<TelegramChatId, VendorAssociation> Snapshot => _chatIdToVendor;

    public async Task EnsureLoadedAsync()
    {
        if (_chatIdToVendor == null)
            await ReloadAsync();
    }

    public async Task ReloadAsync()
    {
        await _reloadSemaphore.WaitAsync(10000);
        try
        {
            var newMappings = new Dictionary<TelegramChatId, VendorAssociation>();

            var allVendors = await _vendorService.GetAllVendorsAsync();

            var allStores = await _storeService.GetAllStoresAsync();

            foreach (var vendor in allVendors)
            {
                foreach (var store in allStores)
                {
                    var storeChannelKey =
                        await _genericAttributeService.GetAttributeAsync<string>(vendor, VENDOR_TELEGRAM_CHANNEL_KEY,
                            store.Id);

                    if (storeChannelKey == null)
                        continue;

                    var storeChannelKeySplit = storeChannelKey?.Split(':');
                    if (storeChannelKeySplit.Length != 2)
                    {
                        await _logger.ErrorAsync(
                            $"Invalid store channel key '{storeChannelKey}' for vendor '{vendor.Name}' and store '{store.Name}'. Should be chatId:threadId");
                        continue;
                    }

                    var storeVendorChatId = new TelegramChatId(long.Parse(storeChannelKeySplit[0]),
                        int.Parse(storeChannelKeySplit[1]));

                    if (newMappings.ContainsKey(storeVendorChatId))
                        await _logger.WarningAsync(
                            $"Duplicate mapping for vendor '{vendor.Name}' and store '{store.Name}'");

                    newMappings[storeVendorChatId] = new VendorAssociation(vendor, store.Id);
                }

                // Backward compatibility
                var channelKey =
                    await _genericAttributeService.GetAttributeAsync<string>(vendor, VENDOR_TELEGRAM_CHANNEL_KEY, 0);
                if (channelKey == null)
                {
                    // Already migrated
                    continue;
                }

                if (long.TryParse(channelKey, out var chatIdLong))
                {
                    // Old version (chatId only, use 0 as threadId)
                    var chatId = new TelegramChatId(chatIdLong, 0);
                    newMappings[chatId] = new VendorAssociation(vendor, allStores.First().Id);

                    await _genericAttributeService.SaveAttributeAsync<string>(vendor, VENDOR_TELEGRAM_CHANNEL_KEY, null, 0);
                    await _genericAttributeService.SaveAttributeAsync(vendor, VENDOR_TELEGRAM_CHANNEL_KEY, $"{chatIdLong}:0",
                        allStores.First().Id);
                    await _logger.InformationAsync(
                        $"Updated telegram channel key for vendor '{vendor.Name}' to store-aware attribute");
                }
                else
                {
                    await _logger.WarningAsync(
                        $"Invalid telegram channel key '{channelKey}' for vendor '{vendor.Name}'. Should be long");
                }
            }

            Interlocked.Exchange(ref _chatIdToVendor, newMappings);

            await _logger.InformationAsync($"Loaded {_chatIdToVendor.Count} chat mappings");
        }
        finally
        {
            _reloadSemaphore.Release();
        }
    }

    public async Task SaveVendorChatMappingAsync(Vendor vendor, int storeId, TelegramChatId chatId)
    {
        await _genericAttributeService.SaveAttributeAsync(vendor, VENDOR_TELEGRAM_CHANNEL_KEY,
            $"{chatId.ChatId}:{chatId.MessageThreadId}", storeId);

        await ReloadAsync();
    }
}

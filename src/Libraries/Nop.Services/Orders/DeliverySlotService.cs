using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Nop.Core.Domain.Orders;
using Nop.Services.Configuration;
using Nop.Services.Logging;

namespace Nop.Services.Orders
{
    /// <summary>
    /// Delivery slot service implementation. Parsing logic mirrors (and is the source of
    /// truth for; see Nop.Plugin.Company.Company.Services.DeliveryTimeService) the format
    /// used by the storefront delivery time picker.
    /// </summary>
    public partial class DeliverySlotService : IDeliverySlotService
    {
        #region Fields

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new TimeSpanJsonConverter() }
        };

        private class TimeSpanJsonConverter : JsonConverter<TimeSpan>
        {
            public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                var value = reader.GetString();
                return TimeSpan.TryParse(value, out var ts) ? ts : TimeSpan.Zero;
            }

            public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options)
            {
                writer.WriteStringValue(value.ToString(@"hh\:mm"));
            }
        }

        private readonly ISettingService _settingService;
        private readonly ILogger _logger;

        #endregion

        #region Ctor

        public DeliverySlotService(ISettingService settingService, ILogger logger)
        {
            _settingService = settingService;
            _logger = logger;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Gets the store's configured delivery slots (enabled only, ordered for display)
        /// </summary>
        public virtual async Task<IList<DeliverySlot>> GetDeliverySlotsAsync(int storeId)
        {
            var orderSettings = await _settingService.LoadSettingAsync<OrderSettings>(storeId);

            if (string.IsNullOrWhiteSpace(orderSettings.ScheduleDate))
                return new List<DeliverySlot>();

            var raw = orderSettings.ScheduleDate.Trim();

            try
            {
                // New JSON format
                if (raw.StartsWith("["))
                {
                    var slots = JsonSerializer.Deserialize<List<DeliverySlot>>(raw, _jsonOptions);
                    return slots?
                        .Where(s => s.IsEnabled)
                        .OrderBy(s => s.SortOrder)
                        .ThenBy(s => s.DeliveryTime)
                        .ToList() ?? new List<DeliverySlot>();
                }

                // Legacy CSV format: "HH:MM:SS-HH:MM:SS-HH:MM:SS,..."
                return ParseLegacyCsv(raw);
            }
            catch (Exception ex)
            {
                await _logger.ErrorAsync($"Error parsing delivery schedule configuration: {raw}", ex);
                return new List<DeliverySlot>();
            }
        }

        /// <summary>
        /// Gets the earliest calendar date a customer could still place an order for
        /// </summary>
        public virtual async Task<DateTime> GetEarliestOrderableDateAsync(int storeId, TimeZoneInfo timeZone)
        {
            var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone);
            var slots = await GetDeliverySlotsAsync(storeId);

            // Nothing configured to restrict against - don't invent a cutoff that isn't there
            if (slots.Count == 0)
                return now.Date;

            var todayStillOrderable = slots.Any(s => now <= now.Date.Add(s.CutoffTime));
            return todayStillOrderable ? now.Date : now.Date.AddDays(1);
        }

        private static List<DeliverySlot> ParseLegacyCsv(string csv)
        {
            var slots = new List<DeliverySlot>();
            var scheduleDateValues = csv.Split(',');
            var sortOrder = 0;

            foreach (var scheduleDate in scheduleDateValues)
            {
                var parts = scheduleDate.Split('-');
                if (parts.Length < 3)
                    continue;

                try
                {
                    var openTime = TimeSpan.Parse(parts[0]);
                    var cutoffTime = TimeSpan.Parse(parts[1]);
                    var deliveryTime = TimeSpan.Parse(parts[2]);

                    slots.Add(new DeliverySlot
                    {
                        OpenTime = openTime,
                        CutoffTime = cutoffTime,
                        DeliveryTime = deliveryTime,
                        IsEnabled = true,
                        SortOrder = sortOrder++
                    });
                }
                catch
                {
                    // Skip malformed entries
                }
            }

            return slots.OrderBy(s => s.DeliveryTime).ToList();
        }

        #endregion
    }
}

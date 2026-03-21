using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Nop.Core.Domain.Orders;
using Nop.Web.Infrastructure.Cache;

namespace Nop.Web.Controllers.Api.Security;

public partial class CatalogApiController
{
    public class ScheduleDatesResponse
    {
        public DateTime FromUtc { get; set; }
        public DateTime ToUtc { get; set; }
        public DateTime DeliveredAtUtc { get; set; }
    }

    [HttpGet("get-schedule-dates")]
    public async Task<IActionResult> GetScheduleDates()
    {
        var storeId = await _storeContext.GetActiveStoreScopeConfigurationAsync();
        var orderSettings = await _settingService.LoadSettingAsync<OrderSettings>(storeId);

        string[] dates = null;
        var scheduleDateSetting = await _settingService.SettingExistsAsync(orderSettings, x => x.ScheduleDate, storeId);
        if (scheduleDateSetting && !string.IsNullOrWhiteSpace(orderSettings.ScheduleDate))
        {
            dates = await _staticCacheManager.GetAsync(
                _staticCacheManager.PrepareKeyForDefaultCache(NopModelCacheDefaults.StoreScheduleDate,
                    await _storeContext.GetCurrentStoreAsync()),
                () =>
                {
                    var raw = orderSettings.ScheduleDate.Trim();
                    var slots = JsonSerializer.Deserialize<JsonElement[]>(raw);
                    return Task.FromResult(slots
                        .Where(s => s.TryGetProperty("isEnabled", out var e) && e.GetBoolean())
                        .Select(s =>
                        {
                            var open = s.GetProperty("openTime").GetString();
                            var cutoff = s.GetProperty("cutoffTime").GetString();
                            var delivery = s.GetProperty("deliveryTime").GetString();
                            return $"{open}:00-{cutoff}:00-{delivery}:00";
                        })
                        .ToArray());
                });

            return Ok(new { success = true, dates });
        }

        return Ok(new
        {
            success = true,
            dates = (string[])null,
            message = await _localizationService.GetResourceAsync("Setting.Not.Found")
        });
    }

    [HttpGet("get-schedules")]
    [ProducesResponseType(typeof(IEnumerable<ScheduleDatesResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<ScheduleDatesResponse>>> GetSchedules()
    {
        var storeId = await _storeContext.GetActiveStoreScopeConfigurationAsync();
        var orderSettings = await _settingService.LoadSettingAsync<OrderSettings>(storeId);

        if (!await _settingService.SettingExistsAsync(orderSettings, x => x.ScheduleDate, storeId))
            return Ok(Enumerable.Empty<ScheduleDatesResponse>());

        var dates = await _staticCacheManager.GetAsync(
            _staticCacheManager.PrepareKeyForDefaultCache(NopModelCacheDefaults.StoreScheduleDate, storeId),
            () =>
            {
                var raw = orderSettings.ScheduleDate?.Trim();
                if (string.IsNullOrWhiteSpace(raw))
                    return Task.FromResult<string[]>(null);

                var slots = JsonSerializer.Deserialize<JsonElement[]>(raw);
                return Task.FromResult(slots
                    .Where(s => s.TryGetProperty("isEnabled", out var e) && e.GetBoolean())
                    .Select(s =>
                    {
                        var open = s.GetProperty("openTime").GetString();
                        var cutoff = s.GetProperty("cutoffTime").GetString();
                        var delivery = s.GetProperty("deliveryTime").GetString();
                        return $"{open}:00-{cutoff}:00-{delivery}:00";
                    })
                    .ToArray());
            });

        if (dates is null)
            return Ok(Enumerable.Empty<ScheduleDatesResponse>());

        var yerevanTz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Yerevan");
        var allowedTimeRange = dates
            .Select(d => d.Split('-'))
            .Where(parts => parts.Length >= 3)
            .Select(splitDates => new ScheduleDatesResponse
            {
                FromUtc = TimeZoneInfo.ConvertTime(DateTime.Parse(splitDates[0]), yerevanTz, TimeZoneInfo.Utc),
                ToUtc = TimeZoneInfo.ConvertTime(DateTime.Parse(splitDates[1]), yerevanTz, TimeZoneInfo.Utc),
                DeliveredAtUtc = TimeZoneInfo.ConvertTime(DateTime.Parse(splitDates[2]), yerevanTz, TimeZoneInfo.Utc)
            });

        return Ok(allowedTimeRange);
    }
}

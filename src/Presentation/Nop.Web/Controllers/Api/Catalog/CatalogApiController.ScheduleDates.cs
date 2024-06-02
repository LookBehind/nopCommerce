using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
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
        if (scheduleDateSetting)
        {
            dates = (await _staticCacheManager.GetAsync(
                _staticCacheManager.PrepareKeyForDefaultCache(NopModelCacheDefaults.StoreScheduleDate,
                    await _storeContext.GetCurrentStoreAsync()),
                async () =>
                {
                    var scheduleDate =
                        await _settingService.GetSettingAsync("ordersettings.scheduledate",
                            (await _storeContext.GetCurrentStoreAsync()).Id,
                            true);
                    return !string.IsNullOrWhiteSpace(scheduleDate.Value) ? scheduleDate.Value.Split(',') : null;
                }));

            return Ok(new { success = true, dates = dates });
        }

        return Ok(new
        {
            success = true,
            dates = dates,
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
            async () =>
            {
                var scheduleDate = await _settingService.GetSettingAsync("ordersettings.scheduledate", 
                    storeId, true);
                
                return !string.IsNullOrWhiteSpace(scheduleDate.Value) ? scheduleDate.Value.Split(',') : null;
            });

        if(dates is null)
            return Ok(Enumerable.Empty<ScheduleDatesResponse>());

        var allowedTimeRange = dates
            .Select(d => d.Split('-'))
            .Select(splitDates => new
            {
                FromUtc = TimeZoneInfo.ConvertTime(DateTime.Parse(splitDates[0]),
                    TimeZoneInfo.FindSystemTimeZoneById("Asia/Yerevan"),
                    TimeZoneInfo.Utc),
                ToUtc = TimeZoneInfo.ConvertTime(DateTime.Parse(splitDates[1]),
                    TimeZoneInfo.FindSystemTimeZoneById("Asia/Yerevan"),
                    TimeZoneInfo.Utc),
                DeliveredAtUtc = TimeZoneInfo.ConvertTime(DateTime.Parse(splitDates[2]),
                    TimeZoneInfo.FindSystemTimeZoneById("Asia/Yerevan"),
                    TimeZoneInfo.Utc)
            });
        
        // Enumerable.Range(0, 14)
        //     .Select(idx => DateTime.UtcNow.AddDays(idx))
        //     .Select(day => new ScheduleDatesResponse()
        //     {
        //         FromUtc = 
        //     })
        
        return Ok();
    }
}
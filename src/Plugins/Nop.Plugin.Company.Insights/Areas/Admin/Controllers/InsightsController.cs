using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Nop.Plugin.Company.Insights.Models;
using Nop.Plugin.Company.Insights.Security;
using Nop.Plugin.Company.Insights.Services;
using Nop.Services.Security;
using Nop.Web.Areas.Admin.Controllers;

namespace Nop.Plugin.Company.Insights.Areas.Admin.Controllers
{
    /// <summary>
    /// Hosts the Insights SPA bootstrap page and its JSON API.
    /// All actions require the <c>AccessInsights</c> permission.
    /// </summary>
    public class InsightsController : BaseAdminController
    {
        private readonly IPermissionService _permissionService;
        private readonly IInsightsReportService _reportService;
        private readonly IInsightsAgentService _agentService;
        private readonly IInsightsMemoryService _memoryService;
        private readonly IInsightsScheduleService _scheduleService;
        private readonly InsightsMemoryConfig _config;
        private readonly IAntiforgery _antiforgery;

        // SPA POSTs are form-encoded (nopCommerce admin filters read Request.Form, which throws on
        // a JSON body); the JSON payload rides in a "payload" form field and is parsed here.
        private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

        // Cache-buster for the bundle: the plugin DLL's build time, so every redeploy serves fresh assets.
        private static readonly string AssetVersion = ResolveAssetVersion();

        private static string ResolveAssetVersion()
        {
            try
            {
                var location = typeof(InsightsController).Assembly.Location;
                if (!string.IsNullOrEmpty(location))
                    return System.IO.File.GetLastWriteTimeUtc(location).Ticks.ToString();
            }
            catch { /* fall through */ }
            return "1";
        }

        public InsightsController(
            IPermissionService permissionService,
            IInsightsReportService reportService,
            IInsightsAgentService agentService,
            IInsightsMemoryService memoryService,
            IInsightsScheduleService scheduleService,
            InsightsMemoryConfig config,
            IAntiforgery antiforgery)
        {
            _permissionService = permissionService;
            _reportService = reportService;
            _agentService = agentService;
            _memoryService = memoryService;
            _scheduleService = scheduleService;
            _config = config;
            _antiforgery = antiforgery;
        }

        private async Task<bool> HasAccessAsync()
        {
            return await _permissionService.AuthorizeAsync(InsightsPermissionProvider.AccessInsights);
        }

        /// <summary>
        /// SPA host page. Served as raw HTML from the controller (not a Razor view) to avoid this
        /// fork's plugin-view-location issue, and to embed the antiforgery request token that the
        /// SPA echoes back in the "RequestVerificationToken" header on its POSTs.
        /// </summary>
        public async Task<IActionResult> Index()
        {
            if (!await HasAccessAsync())
                return AccessDeniedView();

            var token = WebUtility.HtmlEncode(_antiforgery.GetAndStoreTokens(HttpContext).RequestToken ?? string.Empty);
            var v = AssetVersion;
            var html = $@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""utf-8"" />
    <meta name=""viewport"" content=""width=device-width, initial-scale=1, viewport-fit=cover"" />
    <meta name=""request-verification-token"" content=""{token}"" />
    <title>Company Insights</title>
    <link rel=""stylesheet"" href=""/Plugins/Company.Insights/wwwroot/app/main.css?v={v}"" />
</head>
<body>
    <div id=""insights-root"">Loading Insights…</div>
    <script type=""module"" src=""/Plugins/Company.Insights/wwwroot/app/main.js?v={v}""></script>
</body>
</html>";
            return Content(html, "text/html");
        }

        /// <summary>
        /// Connectivity probe the SPA calls on load to confirm the gated API is reachable.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Ping()
        {
            if (!await HasAccessAsync())
                return Json(new { ok = false, error = "access-denied" });

            return Json(new
            {
                ok = true,
                service = "Company.Insights",
                phase = "P4",
                utc = DateTime.UtcNow,
                capabilities = new
                {
                    memory = _memoryService.Enabled,
                    scheduling = _scheduleService.Enabled,
                    telegram = _config.TelegramEnabled
                }
            });
        }

        /// <summary>
        /// Report catalog (no id) or a report result (with id). Matches the
        /// <c>Admin/Insights/Reports/{id?}</c> route.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Reports(string id, int? days, int? limit)
        {
            if (!await HasAccessAsync())
                return StatusCode(StatusCodes.Status403Forbidden);

            if (string.IsNullOrWhiteSpace(id))
            {
                var catalog = _reportService.GetCatalog().Select(r => new
                {
                    id = r.Id,
                    name = r.Name,
                    description = r.Description,
                    defaultChart = r.DefaultChart,
                    xField = r.XField,
                    yField = r.YField,
                    categoryField = r.CategoryField,
                    parameters = r.Parameters.Select(p => new
                    {
                        name = p.Name,
                        label = p.Label,
                        type = p.Type,
                        @default = p.Default,
                        min = p.Min,
                        max = p.Max
                    })
                });
                return Json(catalog);
            }

            var parameters = new Dictionary<string, string>();
            if (days.HasValue)
                parameters["days"] = days.Value.ToString();
            if (limit.HasValue)
                parameters["limit"] = limit.Value.ToString();

            var result = await _reportService.RunAsync(id, parameters);
            if (result == null)
                return NotFound();

            return Json(new
            {
                id = result.Id,
                columns = result.Columns.Select(c => new { name = c.Name, type = c.Type }),
                rows = result.Rows
            });
        }

        /// <summary>
        /// One conversational agent turn. Read-only; gated by the permission. No antiforgery token
        /// required (nopCommerce has no global antiforgery filter and this is a same-origin,
        /// read-only admin API — matching the framework's own admin AJAX endpoints).
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> Chat([FromForm] string payload)
        {
            if (!await HasAccessAsync())
                return StatusCode(StatusCodes.Status403Forbidden);

            var request = Deserialize<ChatTurnRequest>(payload);
            if (request == null)
                return BadRequest();

            var result = await _agentService.RunTurnAsync(request, HttpContext.RequestAborted);

            return Json(new
            {
                reply = result.Reply,
                widgets = result.Widgets.Select(w => new
                {
                    type = w.Type,
                    title = w.Title,
                    chartKind = w.ChartKind,
                    xField = w.XField,
                    yField = w.YField,
                    categoryField = w.CategoryField,
                    columns = w.Columns.Select(c => new { name = c.Name, type = c.Type }),
                    rows = w.Rows
                })
            });
        }

        /// <summary>List scheduled reports for this tenant.</summary>
        [HttpGet]
        public async Task<IActionResult> Schedules()
        {
            if (!await HasAccessAsync())
                return StatusCode(StatusCodes.Status403Forbidden);

            var items = await _scheduleService.ListAsync(HttpContext.RequestAborted);
            return Json(items.Select(MapSchedule));
        }

        /// <summary>Create or update a scheduled report (also (re)registers its Hangfire job).</summary>
        [HttpPost]
        public async Task<IActionResult> SaveSchedule([FromForm] string payload)
        {
            if (!await HasAccessAsync())
                return StatusCode(StatusCodes.Status403Forbidden);

            if (!_scheduleService.Enabled)
                return Json(new { ok = false, error = "scheduling-disabled" });

            var schedule = Deserialize<InsightsSchedule>(payload);
            if (schedule == null || string.IsNullOrWhiteSpace(schedule.ReportId) ||
                string.IsNullOrWhiteSpace(schedule.Cron) || string.IsNullOrWhiteSpace(schedule.TelegramChatId))
                return Json(new { ok = false, error = "missing-fields" });

            var saved = await _scheduleService.UpsertAsync(schedule, HttpContext.RequestAborted);
            return Json(new { ok = true, schedule = MapSchedule(saved) });
        }

        /// <summary>Delete a scheduled report (and remove its Hangfire job).</summary>
        [HttpPost]
        public async Task<IActionResult> DeleteSchedule([FromForm] string id)
        {
            if (!await HasAccessAsync())
                return StatusCode(StatusCodes.Status403Forbidden);

            await _scheduleService.DeleteAsync(id, HttpContext.RequestAborted);
            return Json(new { ok = true });
        }

        private static T Deserialize<T>(string payload) where T : class
        {
            if (string.IsNullOrWhiteSpace(payload))
                return null;
            try { return JsonSerializer.Deserialize<T>(payload, JsonOpts); }
            catch { return null; }
        }

        private static object MapSchedule(InsightsSchedule s) => new
        {
            id = s.Id,
            name = s.Name,
            reportId = s.ReportId,
            cron = s.Cron,
            telegramChatId = s.TelegramChatId,
            enabled = s.Enabled,
            createdAt = s.CreatedAt
        };
    }
}

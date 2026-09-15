using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
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
        private readonly IInsightsProfileService _profileService;
        private readonly IInsightsMemoryService _memoryService;
        private readonly IInsightsWorkspaceService _workspaceService;
        private readonly IInsightsScheduleService _scheduleService;
        private readonly IInsightsScheduleRunner _scheduleRunner;
        private readonly InsightsLlmClient _llmClient;
        private readonly IWorkContext _workContext;
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
            IInsightsProfileService profileService,
            IInsightsMemoryService memoryService,
            IInsightsWorkspaceService workspaceService,
            IInsightsScheduleService scheduleService,
            IInsightsScheduleRunner scheduleRunner,
            InsightsLlmClient llmClient,
            IWorkContext workContext,
            InsightsMemoryConfig config,
            IAntiforgery antiforgery)
        {
            _permissionService = permissionService;
            _reportService = reportService;
            _agentService = agentService;
            _profileService = profileService;
            _memoryService = memoryService;
            _workspaceService = workspaceService;
            _scheduleService = scheduleService;
            _scheduleRunner = scheduleRunner;
            _llmClient = llmClient;
            _workContext = workContext;
            _config = config;
            _antiforgery = antiforgery;
        }

        private async Task<int> CurrentUserIdAsync()
        {
            var customer = await _workContext.GetCurrentCustomerAsync();
            return customer?.Id ?? 0;
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
                    persistence = _workspaceService.Enabled,
                    scheduling = _scheduleService.Enabled,
                    telegram = _config.TelegramEnabled
                }
            });
        }

        /// <summary>
        /// Profiles the current user may use (role-gated) + default + admin/company context.
        /// The profile is the primary interactive context (persona, report/tool set, data scope).
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Profiles()
        {
            if (!await HasAccessAsync())
                return StatusCode(StatusCodes.Status403Forbidden);

            var resolved = await _profileService.ResolveForCurrentUserAsync(HttpContext.RequestAborted);
            return Json(new
            {
                defaultId = resolved.DefaultId,
                isAdmin = resolved.IsAdmin,
                ownCompanyId = resolved.OwnCompanyId,
                profiles = resolved.Allowed.Select(p => new
                {
                    id = p.Id,
                    name = p.Name,
                    description = p.Description,
                    companyScoped = p.CompanyScoped,
                    // A scoped profile needs an explicit company pick only when the user has no own company (admins).
                    needsCompany = p.CompanyScoped && resolved.OwnCompanyId == null
                }),
                companies = resolved.SelectableCompanies.Select(c => new { id = c.Id, name = c.Name })
            });
        }

        /// <summary>
        /// Report catalog (no id) or a report result (with id). Matches the
        /// <c>Admin/Insights/Reports/{id?}</c> route. Scoped to the active profile.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Reports(string id, int? days, int? limit, string from = null, string to = null, string slot = null, string profile = null, int? companyId = null)
        {
            if (!await HasAccessAsync())
                return StatusCode(StatusCodes.Status403Forbidden);

            var activeProfile = await _profileService.GetActiveProfileAsync(profile, HttpContext.RequestAborted);

            if (string.IsNullOrWhiteSpace(id))
            {
                var catalog = _reportService.GetCatalog()
                    .Where(r => activeProfile == null || activeProfile.ReportAllowed(r.Id))
                    .Select(r => new
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

            if (activeProfile != null && !activeProfile.ReportAllowed(id))
                return StatusCode(StatusCodes.Status403Forbidden);

            var parameters = new Dictionary<string, string>();
            if (days.HasValue)
                parameters["days"] = days.Value.ToString();
            if (limit.HasValue)
                parameters["limit"] = limit.Value.ToString();
            if (!string.IsNullOrWhiteSpace(from))
                parameters["from"] = from;
            if (!string.IsNullOrWhiteSpace(to))
                parameters["to"] = to;
            if (!string.IsNullOrWhiteSpace(slot))
                parameters["slot"] = slot;

            var scope = await _profileService.ResolveScopeAsync(activeProfile, companyId, HttpContext.RequestAborted);
            var result = await _reportService.RunAsync(id, parameters, scope);
            if (result == null)
                return NotFound();

            return Json(new
            {
                id = result.Id,
                columns = result.Columns.Select(c => new { name = c.Name, type = c.Type }),
                rows = result.Rows,
                totals = result.Totals?.Select(t => new { label = t.Label, value = t.Value })
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

            var activeProfile = await _profileService.GetActiveProfileAsync(request.ProfileId, HttpContext.RequestAborted);
            var scope = await _profileService.ResolveScopeAsync(activeProfile, request.CompanyId, HttpContext.RequestAborted);
            var result = await _agentService.RunTurnAsync(request, activeProfile, scope, HttpContext.RequestAborted);

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

        // ---- Per-user persistence: workspace ----

        /// <summary>The current user's saved workspace (tabs/widgets/layout), or null.</summary>
        [HttpGet]
        public async Task<IActionResult> Workspace()
        {
            if (!await HasAccessAsync())
                return StatusCode(StatusCodes.Status403Forbidden);

            var json = await _workspaceService.GetWorkspaceAsync(await CurrentUserIdAsync(), HttpContext.RequestAborted);
            return Content(json ?? "null", "application/json");
        }

        /// <summary>Persist the current user's workspace blob.</summary>
        [HttpPost]
        public async Task<IActionResult> SaveWorkspace([FromForm] string payload)
        {
            if (!await HasAccessAsync())
                return StatusCode(StatusCodes.Status403Forbidden);
            if (string.IsNullOrWhiteSpace(payload))
                return Json(new { ok = false, error = "empty" });

            var ok = await _workspaceService.SaveWorkspaceAsync(await CurrentUserIdAsync(), payload, HttpContext.RequestAborted);
            return Json(new { ok });
        }

        // ---- Per-user persistence: conversations ----

        /// <summary>List the current user's saved conversations (headers only).</summary>
        [HttpGet]
        public async Task<IActionResult> Conversations()
        {
            if (!await HasAccessAsync())
                return StatusCode(StatusCodes.Status403Forbidden);

            var items = await _workspaceService.ListConversationsAsync(await CurrentUserIdAsync(), HttpContext.RequestAborted);
            return Json(items.Select(c => new { id = c.Id, title = c.Title, agentId = c.AgentId, createdAt = c.CreatedAt, updatedAt = c.UpdatedAt }));
        }

        /// <summary>Fetch one full conversation (with messages).</summary>
        [HttpGet]
        public async Task<IActionResult> Conversation(string id)
        {
            if (!await HasAccessAsync())
                return StatusCode(StatusCodes.Status403Forbidden);

            var c = await _workspaceService.GetConversationAsync(await CurrentUserIdAsync(), id, HttpContext.RequestAborted);
            if (c == null)
                return NotFound();

            var payload = "{" +
                $"\"id\":{JsonSerializer.Serialize(c.Id)}," +
                $"\"title\":{JsonSerializer.Serialize(c.Title)}," +
                $"\"agentId\":{JsonSerializer.Serialize(c.AgentId)}," +
                $"\"messages\":{(string.IsNullOrWhiteSpace(c.MessagesJson) ? "[]" : c.MessagesJson)}," +
                $"\"createdAt\":{JsonSerializer.Serialize(c.CreatedAt)}," +
                $"\"updatedAt\":{JsonSerializer.Serialize(c.UpdatedAt)}}}";
            return Content(payload, "application/json");
        }

        /// <summary>Create or update a conversation. Payload: {id?, title, agentId, messages:[...]}.</summary>
        [HttpPost]
        public async Task<IActionResult> SaveConversation([FromForm] string payload)
        {
            if (!await HasAccessAsync())
                return StatusCode(StatusCodes.Status403Forbidden);
            if (!_workspaceService.Enabled)
                return Json(new { ok = false, error = "persistence-disabled" });
            if (string.IsNullOrWhiteSpace(payload))
                return Json(new { ok = false, error = "empty" });

            string id = null, title = null, agentId = null, messagesJson = "[]";
            try
            {
                using var doc = JsonDocument.Parse(payload);
                var root = doc.RootElement;
                if (root.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
                    id = idEl.GetString();
                if (root.TryGetProperty("title", out var tEl) && tEl.ValueKind == JsonValueKind.String)
                    title = tEl.GetString();
                if (root.TryGetProperty("agentId", out var aEl) && aEl.ValueKind == JsonValueKind.String)
                    agentId = aEl.GetString();
                if (root.TryGetProperty("messages", out var mEl) && mEl.ValueKind == JsonValueKind.Array)
                    messagesJson = mEl.GetRawText();
            }
            catch
            {
                return Json(new { ok = false, error = "bad-payload" });
            }

            var saved = await _workspaceService.SaveConversationAsync(await CurrentUserIdAsync(), new InsightsConversation
            {
                Id = id,
                Title = title,
                AgentId = agentId,
                MessagesJson = messagesJson
            }, HttpContext.RequestAborted);

            if (saved == null)
                return Json(new { ok = false, error = "save-failed" });
            return Json(new { ok = true, id = saved.Id, updatedAt = saved.UpdatedAt });
        }

        /// <summary>Delete a conversation.</summary>
        [HttpPost]
        public async Task<IActionResult> DeleteConversation([FromForm] string id)
        {
            if (!await HasAccessAsync())
                return StatusCode(StatusCodes.Status403Forbidden);

            await _workspaceService.DeleteConversationAsync(await CurrentUserIdAsync(), id, HttpContext.RequestAborted);
            return Json(new { ok = true });
        }

        // ---- Agent memory management ----

        /// <summary>List saved agent memories for the management UI.</summary>
        [HttpGet]
        public async Task<IActionResult> Memories(string agentId)
        {
            if (!await HasAccessAsync())
                return StatusCode(StatusCodes.Status403Forbidden);

            var items = await _memoryService.ListAsync(agentId ?? "analyst", 100, HttpContext.RequestAborted);
            return Json(items.Select(m => new { id = m.Id, kind = m.Kind, content = m.Content, createdAt = m.CreatedAt }));
        }

        /// <summary>Delete one saved agent memory.</summary>
        [HttpPost]
        public async Task<IActionResult> DeleteMemory([FromForm] string id)
        {
            if (!await HasAccessAsync())
                return StatusCode(StatusCodes.Status403Forbidden);
            if (!long.TryParse(id, out var memId))
                return Json(new { ok = false, error = "bad-id" });

            await _memoryService.DeleteAsync(memId, HttpContext.RequestAborted);
            return Json(new { ok = true });
        }

        // ---- Model warm-up ----

        /// <summary>
        /// Wakes the analysis model (scales from zero) and reports whether it's ready. The SPA polls
        /// this before a chat so it can show progress instead of a long, CDN-timing-out request.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> Warmup()
        {
            if (!await HasAccessAsync())
                return StatusCode(StatusCodes.Status403Forbidden);

            var ready = await _llmClient.ProbeAsync(TimeSpan.FromSeconds(20), HttpContext.RequestAborted);
            return Json(new { ready });
        }

        // ---- Schedule test delivery ----

        /// <summary>Run a schedule definition once and post it to Telegram now (verify before saving).</summary>
        [HttpPost]
        public async Task<IActionResult> TestSchedule([FromForm] string payload)
        {
            if (!await HasAccessAsync())
                return StatusCode(StatusCodes.Status403Forbidden);
            if (!_config.TelegramEnabled)
                return Json(new { ok = false, error = "telegram-disabled" });

            var schedule = Deserialize<InsightsSchedule>(payload);
            if (schedule == null || string.IsNullOrWhiteSpace(schedule.ReportId) ||
                string.IsNullOrWhiteSpace(schedule.TelegramChatId))
                return Json(new { ok = false, error = "missing-fields" });

            var sent = await _scheduleRunner.SendOnceAsync(schedule, HttpContext.RequestAborted);
            return Json(new { ok = sent, error = sent ? null : "send-failed" });
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
            days = s.Days,
            limit = s.Limit,
            createdAt = s.CreatedAt
        };
    }
}

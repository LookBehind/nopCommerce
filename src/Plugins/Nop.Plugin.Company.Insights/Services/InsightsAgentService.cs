using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Nop.Plugin.Company.Insights.Models;
using Nop.Services.Logging;

namespace Nop.Plugin.Company.Insights.Services
{
    /// <summary>
    /// Agent orchestration. The model plans via a strict JSON action protocol over read-only data
    /// tools (list_reports / run_report / query_orders) — no free SQL, no writes — and may finish
    /// with widget proposals bound to the most recent dataset it fetched.
    /// </summary>
    public class InsightsAgentService : IInsightsAgentService
    {
        private const int MaxIterations = 6;
        private const int MaxObservationRows = 50;
        // No token cap on the model (reasoning length is unpredictable), so time is the only bound. A
        // reasoning-heavy answer on the gfx906 GPU can take a while to stream — keep this generous.
        private static readonly TimeSpan LlmTimeout = TimeSpan.FromSeconds(240);

        private readonly InsightsLlmClient _llm;
        private readonly IInsightsReportService _reportService;
        private readonly IInsightsMemoryService _memory;
        private readonly IInsightsAgentConfigService _agents;
        private readonly ILogger _logger;

        public InsightsAgentService(
            InsightsLlmClient llm,
            IInsightsReportService reportService,
            IInsightsMemoryService memory,
            IInsightsAgentConfigService agents,
            ILogger logger)
        {
            _llm = llm;
            _reportService = reportService;
            _memory = memory;
            _agents = agents;
            _logger = logger;
        }

        public async Task<AgentTurnResult> RunTurnAsync(ChatTurnRequest request, InsightsProfile profile, ReportScope scope, Action<string> reportStatus = null, CancellationToken cancellationToken = default)
        {
            profile ??= new InsightsProfile { Id = "analyst", Name = "Analyst", Persona = "a general BI analyst" };
            scope ??= ReportScope.Unscoped();
            var memoryKey = profile.Id;

            var messages = new List<InsightsLlmClient.LlmMessage>
            {
                new InsightsLlmClient.LlmMessage { Role = "system", Content = BuildSystemPrompt(profile, scope, _memory.Enabled, _agents.Enabled) }
            };

            foreach (var m in request.Messages ?? new List<AgentChatMessage>())
            {
                if (string.IsNullOrWhiteSpace(m?.Content))
                    continue;
                messages.Add(new InsightsLlmClient.LlmMessage
                {
                    Role = string.Equals(m.Role, "assistant", StringComparison.OrdinalIgnoreCase) ? "assistant" : "user",
                    Content = m.Content
                });
            }

            await InjectMemoryContextAsync(messages, request, memoryKey, cancellationToken);

            return await RunLoopAsync(messages, memoryKey, scope, allowWidgets: true, readOnly: false, automationsEnabled: _agents.Enabled, reportStatus, cancellationToken);
        }

        public async Task<string> RunBackgroundAsync(InsightsAgentConfig config, string triggerJson, ReportScope scope, CancellationToken cancellationToken = default)
        {
            scope ??= ReportScope.Unscoped();
            var sb = new StringBuilder();
            sb.AppendLine((config.SystemPrompt ?? "You are a background analytics agent for the MySnacks platform. You are READ-ONLY.").Trim());
            if (scope.CompanyId.HasValue)
                sb.AppendLine("Your scope is a single company; every tool already returns only that company's data.");
            sb.AppendLine("Ground your answer in real data: use the read-only tools to look up the products / orders / reviews the trigger refers to before drawing conclusions. Do not invent facts (e.g. never claim a product is missing an image or description without checking with list_products).");
            sb.AppendLine("If, after checking the data, there is nothing worth sending (no issue, nothing actionable, everything looks fine), answer with EXACTLY the text NO_REPORT and nothing else — the run is recorded but no message is sent to the outputs. Only produce a full report when it is genuinely useful; don't send noise.");
            AppendProtocolAndTools(sb, scope, _memory.Enabled, automationsEnabled: false, includeWidgets: false);

            var messages = new List<InsightsLlmClient.LlmMessage>
            {
                new InsightsLlmClient.LlmMessage { Role = "system", Content = sb.ToString() },
                new InsightsLlmClient.LlmMessage
                {
                    Role = "user",
                    Content = (config.Instruction ?? "Analyse the trigger and produce a concise, actionable note.").Trim()
                              + "\n\nTrigger context (JSON):\n" + triggerJson
                }
            };

            var result = await RunLoopAsync(messages, config.Id, scope, allowWidgets: false, readOnly: true, automationsEnabled: false, null, cancellationToken);
            return result.Reply;
        }

        // Read-only agents can't call these write tools even if the model tries.
        private static readonly HashSet<string> WriteTools = new(StringComparer.OrdinalIgnoreCase)
        { "remember", "create_automation", "update_automation", "delete_automation" };

        /// <summary>The shared native tool-calling loop used by both the interactive chat and background
        /// agents. The model calls read-only tools via the OpenAI/vLLM function-calling interface (parsed by
        /// vLLM's qwen3_coder tool-call parser into structured tool_calls) until it answers with content.</summary>
        private async Task<AgentTurnResult> RunLoopAsync(List<InsightsLlmClient.LlmMessage> messages, string memoryKey,
            ReportScope scope, bool allowWidgets, bool readOnly, bool automationsEnabled, Action<string> reportStatus,
            CancellationToken cancellationToken)
        {
            void Report(string s) { try { reportStatus?.Invoke(s); } catch { /* status is best-effort */ } }
            InsightsReportResult lastDataset = null;
            var pendingWidgets = new List<WidgetProposal>();
            var tools = BuildToolSchemas(scope, _memory.Enabled, automationsEnabled, allowWidgets);

            try
            {
                for (var i = 0; i < MaxIterations; i++)
                {
                    Report(i == 0 ? "Thinking…" : "Analyzing…");
                    var completion = await _llm.CompleteWithToolsAsync(
                        InsightsLlmClient.DefaultModel, messages, 0.0, tools, LlmTimeout, cancellationToken);

                    // Model answered (no tool calls) → that content is the reply (plain Markdown). Keep a
                    // tolerant fallback for the old {"final":...} JSON envelope and any stray native markup.
                    if (!completion.HasToolCalls)
                        return FinishAnswer(completion.Content ?? "", allowWidgets, pendingWidgets, lastDataset);

                    // Echo the assistant tool-call message, then run each tool and feed the result back as a
                    // role:"tool" message (required by the tool-calling protocol) so the model can continue.
                    messages.Add(new InsightsLlmClient.LlmMessage
                    {
                        Role = "assistant",
                        Content = completion.Content,
                        ToolCalls = completion.ToolCalls.ToList()
                    });

                    foreach (var call in completion.ToolCalls)
                    {
                        var tool = call.Function?.Name ?? "";
                        Report(ToolStatusLabel(tool));
                        JsonDocument argsDoc = null;
                        try
                        {
                            argsDoc = ParseToolArgs(call.Function?.Arguments);
                            var argsEl = argsDoc?.RootElement ?? default;
                            string observation;

                            if (allowWidgets && tool == "propose_widget")
                            {
                                // Meta-tool: attach a chart/table to the answer (visualizes the last dataset).
                                var widget = BuildWidget(argsEl, lastDataset);
                                if (widget == null)
                                    observation = "error: fetch a dataset with a data tool first, then propose a widget for it.";
                                else
                                {
                                    pendingWidgets.Add(widget);
                                    observation = $"ok: '{widget.Title}' widget attached to your answer.";
                                }
                            }
                            else if (readOnly && WriteTools.Contains(tool))
                            {
                                observation = $"error: '{tool}' is not available to a background automation (read-only). Use it only to read data.";
                            }
                            else
                            {
                                InsightsReportResult dataset = null;
                                try
                                {
                                    (observation, dataset) = await ExecuteToolAsync(tool, argsEl, memoryKey, scope, cancellationToken);
                                }
                                catch (Exception toolEx) when (!(toolEx is OperationCanceledException && cancellationToken.IsCancellationRequested))
                                {
                                    // A tool failure shouldn't sink the whole turn — feed it back so the model can adapt.
                                    await _logger.WarningAsync($"Insights agent tool '{tool}' failed", toolEx);
                                    observation = $"error: the '{tool}' tool failed ({toolEx.Message}). Try a different tool or answer with what you already have.";
                                }
                                if (dataset != null)
                                    lastDataset = dataset;
                            }

                            messages.Add(new InsightsLlmClient.LlmMessage
                            {
                                Role = "tool",
                                ToolCallId = call.Id,
                                Name = tool,
                                Content = observation
                            });
                        }
                        finally
                        {
                            argsDoc?.Dispose();
                        }
                    }
                }

                return new AgentTurnResult
                {
                    Reply = "I couldn't finish that within a few steps — try a more specific question."
                };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw; // the user cancelled (Stop) — let the controller handle it, don't show an error
            }
            catch (Exception ex)
            {
                await _logger.ErrorAsync("Insights agent turn failed", ex);
                // Distinguish a slow/unreachable model from a real bug (the model is pinned warm — no cold
                // start — so a timeout means the request genuinely ran long or the gateway was unreachable).
                var modelUnreachable = ex is OperationCanceledException || ex is TaskCanceledException
                    || ex is HttpRequestException || ex is TimeoutException;
                return new AgentTurnResult
                {
                    Reply = modelUnreachable
                        ? "The analysis model didn't respond in time — that question may be too heavy. Try narrowing it or asking again."
                        : "Something went wrong while answering that. Please try again, or rephrase your question."
                };
            }
        }

        public async Task<string> DraftAgentAsync(string description, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(description))
                return null;

            var sb = new StringBuilder();
            sb.AppendLine("You design a background automation for the MySnacks platform from the user's request.");
            sb.AppendLine("Respond with EXACTLY ONE JSON object and nothing else — no markdown, no prose.");
            sb.AppendLine("Shape:");
            sb.AppendLine("{\"name\":\"short title\",\"triggerKind\":\"event\"|\"schedule\",\"eventType\":\"<one of the events, for event kind>\",\"cron\":\"<5-field cron, for schedule kind>\",\"filter\":{},\"systemPrompt\":\"the background agent's own system prompt (concise, read-only analyst persona for its task)\",\"instruction\":\"the task the agent performs each run\",\"outputSinks\":[\"dashboard\"|\"telegram\"|\"memory\"],\"outputTarget\":\"telegram chat id if telegram, else empty\"}");
            sb.AppendLine("Event types: review-added, review-triaged, order-placed, order-cancelled, delivery-approaching (40 min before a delivery), day-closing (end of day), product-created, product-updated.");
            sb.AppendLine("Filters (optional, event kind): {\"maxRating\":<int>} fires only for reviews at/below that rating; {\"vendorIds\":[<int>...]} restricts to those vendors.");
            sb.AppendLine("Pick triggerKind \"schedule\" only if the user asks for a periodic/scheduled run; else \"event\". Choose the single best eventType. Default outputSinks to [\"dashboard\"].");
            sb.AppendLine("Write a genuinely useful systemPrompt + instruction for the described task. The agent is READ-ONLY (it can never modify data).");

            var messages = new List<InsightsLlmClient.LlmMessage>
            {
                new InsightsLlmClient.LlmMessage { Role = "system", Content = sb.ToString() },
                new InsightsLlmClient.LlmMessage { Role = "user", Content = description.Trim() }
            };

            try
            {
                var content = await _llm.CompleteAsync(InsightsLlmClient.DefaultModel, messages, 0.2, null, LlmTimeout, cancellationToken);
                var json = ExtractJsonObject(content);
                if (json == null)
                    return null;
                // Re-emit as canonical JSON so raw newlines in systemPrompt/instruction don't break the caller's parse.
                using var doc = TryParseJsonObject(json);
                return doc?.RootElement.GetRawText() ?? json;
            }
            catch (Exception ex)
            {
                await _logger.WarningAsync("Insights meta-agent: draft failed", ex);
                return null;
            }
        }

        #region Tools

        /// <summary>A short, user-facing label for the tool currently running (shown in the chat while it works).</summary>
        private static string ToolStatusLabel(string tool) => tool switch
        {
            "list_reports" => "Looking up available reports…",
            "run_report" => "Running a report…",
            "query_orders" => "Querying orders…",
            "list_reviews" => "Reading reviews…",
            "list_products" => "Looking up products…",
            "list_orders" => "Looking up orders…",
            "recall" => "Recalling notes…",
            "remember" => "Saving a note…",
            "list_automations" => "Listing automations…",
            "create_automation" => "Creating an automation…",
            "update_automation" => "Updating an automation…",
            "delete_automation" => "Deleting an automation…",
            _ => "Working…"
        };

        private async Task<(string observation, InsightsReportResult dataset)> ExecuteToolAsync(
            string tool, JsonElement args, string memoryKey, ReportScope scope, CancellationToken ct)
        {
            switch (tool)
            {
                case "remember":
                {
                    var content = GetString(args, "content");
                    if (string.IsNullOrWhiteSpace(content))
                        return ("error: missing 'content'", null);
                    var ok = await _memory.RememberAsync(memoryKey, GetString(args, "kind"), content, ct);
                    return (ok ? "saved" : "error: memory unavailable", null);
                }
                case "recall":
                {
                    var q = GetString(args, "query");
                    if (string.IsNullOrWhiteSpace(q))
                        return ("error: missing 'query'", null);
                    var mems = await _memory.RecallAsync(memoryKey, q, 5, ct);
                    return (JsonSerializer.Serialize(mems.Select(m => new { m.Content, m.Kind })), null);
                }
                case "list_reports":
                {
                    var catalog = _reportService.GetCatalog().Select(r => new
                    {
                        r.Id,
                        r.Name,
                        r.Description,
                        r.DefaultChart,
                        Parameters = r.Parameters.Select(p => new { p.Name, p.Default, p.Min, p.Max })
                    });
                    return (JsonSerializer.Serialize(catalog), null);
                }
                case "run_report":
                {
                    var id = GetString(args, "id");
                    if (string.IsNullOrWhiteSpace(id))
                        return ("error: missing 'id'", null);
                    var parameters = new Dictionary<string, string>();
                    var days = GetInt(args, "days");
                    if (days.HasValue)
                        parameters["days"] = days.Value.ToString();
                    var result = await _reportService.RunAsync(id, parameters, scope);
                    if (result == null)
                        return ($"error: unknown report id '{id}'", null);
                    return (SummarizeDataset(result), result);
                }
                case "query_orders":
                {
                    var days = GetInt(args, "days") ?? 30;
                    var groupBy = GetString(args, "groupBy") ?? "day";
                    var metric = GetString(args, "metric") ?? "count";
                    var result = await _reportService.QueryOrdersAsync(days, groupBy, metric, scope);
                    return (SummarizeDataset(result), result);
                }
                case "list_reviews":
                {
                    var days = GetInt(args, "days") ?? 30;
                    var vendorId = GetInt(args, "vendorId");
                    var customerEmail = GetString(args, "customerEmail");
                    var customerName = GetString(args, "customerName");
                    var orderBy = GetString(args, "orderBy") ?? "date";
                    var limit = GetInt(args, "limit");
                    var result = await _reportService.GetReviewsAsync(days, vendorId, customerEmail, customerName, orderBy, limit, scope);
                    return (SummarizeDataset(result), result);
                }
                case "list_products":
                {
                    var result = await _reportService.ListProductsAsync(
                        GetInt(args, "id"), GetInt(args, "vendorId"), GetString(args, "name"),
                        GetString(args, "category"), GetString(args, "orderBy"), GetInt(args, "limit"), scope);
                    return (SummarizeDataset(result), result);
                }
                case "list_orders":
                {
                    var result = await _reportService.ListOrdersAsync(
                        GetString(args, "from"), GetString(args, "to"), GetString(args, "status"),
                        GetInt(args, "limit"), scope,
                        GetString(args, "customerEmail"), GetString(args, "customerName"),
                        GetInt(args, "vendorId"), GetInt(args, "productId"));
                    return (SummarizeDataset(result), result);
                }
                case "list_automations":
                {
                    if (!_agents.Enabled)
                        return ("error: automations are unavailable (no database configured)", null);
                    var all = await _agents.ListAsync(ct);
                    var visible = all.Where(c => CanManageAutomation(c, scope)).Select(c => new
                    {
                        c.Id, c.Name, c.Enabled, c.BuiltIn, triggerKind = c.TriggerKind, eventType = c.EventType,
                        c.Cron, companyId = c.CompanyId, filter = c.FilterJson, outputSinks = c.OutputSinksJson, c.Instruction
                    });
                    return (JsonSerializer.Serialize(visible), null);
                }
                case "create_automation":
                {
                    if (!_agents.Enabled)
                        return ("error: automations are unavailable (no database configured)", null);
                    if (scope != null && scope.Denied)
                        return ("error: not permitted", null);
                    var c = BuildAutomationFromArgs(args);
                    if (string.IsNullOrWhiteSpace(c.Name))
                        return ("error: missing 'name'", null);
                    if (c.TriggerKind == "event" && string.IsNullOrWhiteSpace(c.EventType))
                        return ("error: an event automation needs an 'eventType'", null);
                    if (c.TriggerKind == "schedule" && string.IsNullOrWhiteSpace(c.Cron))
                        return ("error: a schedule automation needs a 'cron'", null);
                    if (string.IsNullOrWhiteSpace(c.Instruction))
                        return ("error: missing 'instruction' (what the automation should do each run)", null);
                    // Company-scoped user (Workplace Manager) can only create automations for their own company;
                    // Backoffice may leave it global (null) or target a specific company.
                    c.CompanyId = scope?.CompanyId ?? GetInt(args, "companyId");
                    var saved = await _agents.UpsertAsync(c, ct);
                    return saved == null ? ("error: could not save the automation", null)
                        : ($"created automation '{saved.Name}' (id {saved.Id}, {(saved.Enabled ? "enabled" : "disabled")}).", null);
                }
                case "update_automation":
                {
                    if (!_agents.Enabled)
                        return ("error: automations are unavailable (no database configured)", null);
                    var id = GetString(args, "id");
                    if (string.IsNullOrWhiteSpace(id))
                        return ("error: missing 'id'", null);
                    var existing = await _agents.GetAsync(id, ct);
                    if (existing == null)
                        return ($"error: no automation with id '{id}'", null);
                    if (!CanManageAutomation(existing, scope))
                        return ("error: that automation belongs to another company — not permitted", null);
                    if (existing.BuiltIn && scope?.CompanyId != null)
                        return ("error: built-in automations can only be managed by backoffice support", null);
                    // Merge only the fields the caller actually supplied.
                    var patch = BuildAutomationFromArgs(args);
                    if (!string.IsNullOrWhiteSpace(patch.Name)) existing.Name = patch.Name;
                    if (HasProp(args, "enabled")) existing.Enabled = GetBool(args, "enabled") ?? existing.Enabled;
                    if (HasProp(args, "triggerKind")) existing.TriggerKind = patch.TriggerKind;
                    if (HasProp(args, "eventType")) existing.EventType = patch.EventType;
                    if (HasProp(args, "cron")) existing.Cron = patch.Cron;
                    if (HasProp(args, "filter")) existing.FilterJson = patch.FilterJson;
                    if (!string.IsNullOrWhiteSpace(patch.SystemPrompt)) existing.SystemPrompt = patch.SystemPrompt;
                    if (!string.IsNullOrWhiteSpace(patch.Instruction)) existing.Instruction = patch.Instruction;
                    if (HasProp(args, "outputSinks")) existing.OutputSinksJson = patch.OutputSinksJson;
                    if (HasProp(args, "outputTarget")) existing.OutputTarget = patch.OutputTarget;
                    // A company user cannot move an automation to another company.
                    if (scope?.CompanyId != null) existing.CompanyId = scope.CompanyId;
                    var saved = await _agents.UpsertAsync(existing, ct);
                    return ($"updated automation '{saved.Name}' (id {saved.Id}, {(saved.Enabled ? "enabled" : "disabled")}).", null);
                }
                case "delete_automation":
                {
                    if (!_agents.Enabled)
                        return ("error: automations are unavailable (no database configured)", null);
                    var id = GetString(args, "id");
                    if (string.IsNullOrWhiteSpace(id))
                        return ("error: missing 'id'", null);
                    var existing = await _agents.GetAsync(id, ct);
                    if (existing == null)
                        return ($"error: no automation with id '{id}'", null);
                    if (!CanManageAutomation(existing, scope))
                        return ("error: that automation belongs to another company — not permitted", null);
                    if (existing.BuiltIn && scope?.CompanyId != null)
                        return ("error: built-in automations can only be managed by backoffice support", null);
                    await _agents.DeleteAsync(id, ct);
                    return ($"deleted automation '{existing.Name}'.", null);
                }
                default:
                    return ($"error: '{tool}' is not a tool. Valid tools: list_reports, run_report, query_orders, "
                        + "list_reviews, list_products, list_orders, recall, remember, list_automations, create_automation, "
                        + "update_automation, delete_automation. Call one with {\"action\":\"<tool>\",\"args\":{...}}.", null);
            }
        }

        /// <summary>Automation access rule: backoffice (unscoped) manages all; a company-scoped user (Workplace
        /// Manager) manages only automations tied to their own company. Denied scope manages nothing.</summary>
        private static bool CanManageAutomation(InsightsAgentConfig c, ReportScope scope)
        {
            if (scope == null || scope.CompanyId == null) return scope == null || !scope.Denied;
            if (scope.Denied) return false;
            return c.CompanyId == scope.CompanyId;
        }

        /// <summary>Builds an automation config from tool args (fields the caller omits stay null/default).</summary>
        private static InsightsAgentConfig BuildAutomationFromArgs(JsonElement args)
        {
            var kind = (GetString(args, "triggerKind") ?? "event").Trim().ToLowerInvariant();
            var c = new InsightsAgentConfig
            {
                Name = GetString(args, "name"),
                TriggerKind = kind == "schedule" ? "schedule" : "event",
                EventType = GetString(args, "eventType"),
                Cron = GetString(args, "cron"),
                SystemPrompt = GetString(args, "systemPrompt"),
                Instruction = GetString(args, "instruction"),
                OutputTarget = GetString(args, "outputTarget"),
                Enabled = GetBool(args, "enabled") ?? true
            };
            if (args.ValueKind == JsonValueKind.Object)
            {
                if (args.TryGetProperty("filter", out var f) && f.ValueKind == JsonValueKind.Object)
                    c.FilterJson = f.GetRawText();
                if (args.TryGetProperty("outputSinks", out var s) && s.ValueKind == JsonValueKind.Array)
                    c.OutputSinksJson = s.GetRawText();
            }
            return c;
        }

        /// <summary>Auto-recalls relevant saved notes for the latest user message and injects them as context.</summary>
        private async Task InjectMemoryContextAsync(
            List<InsightsLlmClient.LlmMessage> messages, ChatTurnRequest request, string memoryKey, CancellationToken ct)
        {
            if (!_memory.Enabled)
                return;

            var lastUser = (request.Messages ?? new List<AgentChatMessage>())
                .LastOrDefault(m => string.Equals(m?.Role, "user", StringComparison.OrdinalIgnoreCase));
            if (lastUser == null || string.IsNullOrWhiteSpace(lastUser.Content))
                return;

            var memories = await _memory.RecallAsync(memoryKey, lastUser.Content, 3, ct);
            if (memories.Count == 0)
                return;

            var sb = new StringBuilder("\n\nNotes you saved in earlier conversations (use if relevant, ignore otherwise):\n");
            foreach (var m in memories)
                sb.AppendLine("- " + m.Content);

            // Append to the persona system prompt rather than adding a SECOND system message — the Qwen3
            // chat template rejects more than one system message (returns HTTP 400), which would silently
            // break every memory-augmented turn.
            var system = messages.FirstOrDefault(m => string.Equals(m.Role, "system", StringComparison.OrdinalIgnoreCase));
            if (system != null)
                system.Content += sb.ToString();
            else
                messages.Insert(0, new InsightsLlmClient.LlmMessage { Role = "system", Content = sb.ToString() });
        }

        private static string SummarizeDataset(InsightsReportResult result)
        {
            var payload = new
            {
                columns = result.Columns.Select(c => new { c.Name, c.Type }),
                rowCount = result.Rows.Count,
                rows = result.Rows.Take(MaxObservationRows)
            };
            return JsonSerializer.Serialize(payload);
        }

        private static List<WidgetProposal> BuildWidgets(JsonElement widgetsEl, InsightsReportResult dataset)
        {
            var list = new List<WidgetProposal>();
            if (dataset == null || widgetsEl.ValueKind != JsonValueKind.Array)
                return list;

            foreach (var w in widgetsEl.EnumerateArray())
            {
                var widget = BuildWidget(w, dataset);
                if (widget != null)
                    list.Add(widget);
            }
            return list;
        }

        /// <summary>Build one widget from a spec object bound to a dataset (for the propose_widget tool).</summary>
        private static WidgetProposal BuildWidget(JsonElement w, InsightsReportResult dataset)
        {
            if (dataset == null || w.ValueKind != JsonValueKind.Object)
                return null;
            var type = GetString(w, "kind") ?? "chart";
            return new WidgetProposal
            {
                Type = type == "table" ? "table" : "chart",
                Title = GetString(w, "title") ?? "Result",
                ChartKind = GetString(w, "chartKind") ?? "line",
                XField = GetString(w, "xField"),
                YField = GetString(w, "yField"),
                CategoryField = GetString(w, "categoryField"),
                Columns = dataset.Columns,
                Rows = dataset.Rows
            };
        }

        #endregion

        #region Prompt & JSON helpers

        private static string BuildSystemPrompt(InsightsProfile profile, ReportScope scope, bool memoryEnabled, bool automationsEnabled)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"You are {profile.Name}, {profile.Persona} on the MySnacks food-ordering platform.");
            sb.AppendLine("You help backoffice staff explore this tenant's order data. You are READ-ONLY: you can only read data through the tools below, never modify anything.");
            if (scope != null && scope.CompanyId.HasValue)
                sb.AppendLine("IMPORTANT: your data is already restricted to a single company — every tool only returns that company's data. Do not claim to see other companies.");
            AppendProtocolAndTools(sb, scope, memoryEnabled, automationsEnabled, includeWidgets: true);
            return sb.ToString();
        }

        /// <summary>Behavioural guidance + tool policy shared by the interactive chat and the background
        /// automations. The tool schemas themselves are passed natively via <see cref="BuildToolSchemas"/>;
        /// this only tells the model how/when to use them and how to format the final answer. Background
        /// agents pass includeWidgets:false and automationsEnabled:false.</summary>
        private static void AppendProtocolAndTools(StringBuilder sb, ReportScope scope, bool memoryEnabled, bool automationsEnabled, bool includeWidgets)
        {
            sb.AppendLine();
            sb.AppendLine("You have READ-ONLY data tools, available through the function-calling interface — call them to fetch real data, and NEVER print a tool call as text. Always ground every figure in a tool result; never invent numbers, product details, images or descriptions.");
            sb.AppendLine("Key tools: list_reports / run_report (named reports), query_orders (aggregated orders), list_orders (individual orders by delivery date), list_products (product details incl. pictures), list_reviews (reviews + triage). Refer to customers AND vendors by their name and email, never a bare id.");
            if (memoryEnabled)
                sb.AppendLine("You can recall notes from earlier sessions" + (includeWidgets ? ", and remember durable learnings for the future" : "") + ".");
            if (automationsEnabled)
            {
                sb.AppendLine();
                sb.AppendLine("You can fully manage automations (background agents that run on a trigger and write to an output sink) with list_automations / create_automation / update_automation / delete_automation. When you create one, YOU write its concise read-only systemPrompt + instruction. Event types: review-added, review-triaged, order-placed, order-cancelled, delivery-approaching, day-closing, product-created, product-updated.");
                if (scope != null && scope.CompanyId.HasValue)
                    sb.AppendLine("You may only manage automations for your own company; new ones you create are automatically scoped to it. Confirm deletes with the user first.");
                else
                    sb.AppendLine("You manage automations across all companies (leave companyId unset for a global automation). Confirm deletes with the user first.");
            }
            if (includeWidgets)
                sb.AppendLine("To attach a chart or table to your answer, call propose_widget (it visualizes the MOST RECENT dataset you fetched); its field names must match that dataset's columns. Only do so when a chart or table genuinely helps.");
            sb.AppendLine();
            sb.AppendLine("When you have enough data, STOP calling tools and write your answer directly as Markdown (no JSON, no envelope): lead with a one-line takeaway, then a bulleted list ('- ') of the key points — one figure or finding per bullet, with **bold** on the important numbers, names and labels." + (includeWidgets ? " Prefer a widget over a large Markdown table." : ""));
        }

        /// <summary>Native tool schemas (OpenAI function-calling shape) offered to the model — the read-only
        /// data tools plus, per flags, memory, widget, and automation-management tools.</summary>
        private static List<InsightsLlmClient.LlmTool> BuildToolSchemas(ReportScope scope, bool memoryEnabled, bool automationsEnabled, bool includeWidgets)
        {
            InsightsLlmClient.LlmTool Fn(string name, string desc, Dictionary<string, object> props, string[] required = null)
            {
                var parameters = new Dictionary<string, object> { ["type"] = "object", ["properties"] = props ?? new Dictionary<string, object>() };
                if (required != null && required.Length > 0)
                    parameters["required"] = required;
                return new InsightsLlmClient.LlmTool { Function = new InsightsLlmClient.LlmFunctionDef { Name = name, Description = desc, Parameters = parameters } };
            }
            Dictionary<string, object> Str(string desc) => new() { ["type"] = "string", ["description"] = desc };
            Dictionary<string, object> Int(string desc) => new() { ["type"] = "integer", ["description"] = desc };
            Dictionary<string, object> Bool(string desc) => new() { ["type"] = "boolean", ["description"] = desc };
            Dictionary<string, object> Sel(string desc, params string[] vals) => new() { ["type"] = "string", ["description"] = desc, ["enum"] = vals };
            Dictionary<string, object> StrArray(string desc) => new() { ["type"] = "array", ["items"] = new Dictionary<string, object> { ["type"] = "string" }, ["description"] = desc };
            Dictionary<string, object> Obj(string desc) => new() { ["type"] = "object", ["description"] = desc };

            var tools = new List<InsightsLlmClient.LlmTool>
            {
                Fn("list_reports", "List the available named reports and their parameters (with min/max).", new Dictionary<string, object>()),
                Fn("run_report", "Run a named report and return its columns and rows.", new Dictionary<string, object>
                {
                    ["id"] = Str("the report id (from list_reports)"),
                    ["days"] = Int("optional lookback window, clamped to the report's allowed range (e.g. up to 90)")
                }, new[] { "id" }),
                Fn("query_orders", "Aggregated orders over the last N days.", new Dictionary<string, object>
                {
                    ["days"] = Int("optional, default 30, max 365"),
                    ["groupBy"] = Sel("how to group the counts", "day", "status"),
                    ["metric"] = Sel("what to aggregate", "count", "revenue")
                }),
                Fn("list_reviews", "Product reviews with vendor name+email and customer full name+email, rating, approval, title, body and triage (who/when/hours/resolution).", new Dictionary<string, object>
                {
                    ["days"] = Int("optional, default 30, max 90"),
                    ["vendorId"] = Int("optional vendor filter"),
                    ["customerEmail"] = Str("optional customer email filter"),
                    ["customerName"] = Str("optional customer name filter"),
                    ["orderBy"] = Sel("sort order", "date", "rating", "helpful"),
                    ["limit"] = Int("optional, default 50, max 200")
                }),
                Fn("list_products", "Products with vendor name+email, SKU, price, weight, published, categories, short + full description (HTML stripped) and picture URLs. Use it to check a product's real details before commenting.", new Dictionary<string, object>
                {
                    ["id"] = Int("optional exact product id"),
                    ["vendorId"] = Int("optional vendor filter"),
                    ["name"] = Str("optional name substring"),
                    ["category"] = Str("optional category name or id"),
                    ["orderBy"] = Sel("sort order", "name", "price", "created", "id"),
                    ["limit"] = Int("optional, default 20, max 50")
                }),
                Fn("list_orders", "Individual orders by delivery date: id, created, delivery time, status, total, customer name+email and an item summary.", new Dictionary<string, object>
                {
                    ["from"] = Str("optional delivery date from, YYYY-MM-DD"),
                    ["to"] = Str("optional delivery date to, YYYY-MM-DD"),
                    ["status"] = Str("optional order status id"),
                    ["customerEmail"] = Str("optional customer email filter"),
                    ["customerName"] = Str("optional customer name filter"),
                    ["vendorId"] = Int("optional — orders containing that vendor's products"),
                    ["productId"] = Int("optional — orders containing that product"),
                    ["limit"] = Int("optional, default 25, max 100")
                })
            };

            if (memoryEnabled)
            {
                tools.Add(Fn("recall", "Retrieve notes saved in earlier runs/conversations.", new Dictionary<string, object> { ["query"] = Str("what to look up") }, new[] { "query" }));
                if (includeWidgets)
                    tools.Add(Fn("remember", "Save a durable, reusable learning for future conversations.", new Dictionary<string, object>
                    {
                        ["content"] = Str("the note to remember"),
                        ["kind"] = Str("optional category, e.g. note")
                    }, new[] { "content" }));
            }

            if (includeWidgets)
                tools.Add(Fn("propose_widget", "Attach a chart or table (of the MOST RECENT dataset you fetched) to your answer. Field names must match that dataset's columns.", new Dictionary<string, object>
                {
                    ["kind"] = Sel("widget kind", "chart", "table"),
                    ["chartKind"] = Sel("chart type (for kind=chart)", "line", "area", "bar", "pie"),
                    ["title"] = Str("widget title"),
                    ["xField"] = Str("x-axis column"),
                    ["yField"] = Str("y-axis column"),
                    ["categoryField"] = Str("optional series/category column")
                }));

            if (automationsEnabled)
            {
                tools.Add(Fn("list_automations", "List the automations you can manage (id, name, enabled, trigger, schedule/event, filter, instruction).", new Dictionary<string, object>()));
                tools.Add(Fn("create_automation", "Create a background automation. YOU write a concise READ-ONLY systemPrompt + instruction for the task.", new Dictionary<string, object>
                {
                    ["name"] = Str("automation name"),
                    ["triggerKind"] = Sel("trigger kind", "event", "schedule"),
                    ["eventType"] = Str("event type (for triggerKind=event): review-added, review-triaged, order-placed, order-cancelled, delivery-approaching, day-closing, product-created, product-updated"),
                    ["cron"] = Str("5-field cron in Asia/Yerevan (for triggerKind=schedule)"),
                    ["filter"] = Obj("optional {\"maxRating\":<int>} and/or {\"vendorIds\":[<int>...]}"),
                    ["systemPrompt"] = Str("the background agent's own concise read-only persona"),
                    ["instruction"] = Str("what it does each run"),
                    ["outputSinks"] = StrArray("any of dashboard, telegram, memory"),
                    ["outputTarget"] = Str("telegram chat id (if telegram sink)")
                }, new[] { "name", "triggerKind", "instruction" }));
                tools.Add(Fn("update_automation", "Edit an automation — pass id plus only the fields to change (e.g. enabled:false or a new cron).", new Dictionary<string, object>
                {
                    ["id"] = Str("automation id"),
                    ["name"] = Str("optional new name"),
                    ["enabled"] = Bool("optional enable/disable"),
                    ["eventType"] = Str("optional new event type"),
                    ["cron"] = Str("optional new cron"),
                    ["instruction"] = Str("optional new instruction"),
                    ["systemPrompt"] = Str("optional new system prompt"),
                    ["outputSinks"] = StrArray("optional new sinks"),
                    ["outputTarget"] = Str("optional telegram chat id")
                }, new[] { "id" }));
                tools.Add(Fn("delete_automation", "Delete an automation by id. Confirm with the user first.", new Dictionary<string, object> { ["id"] = Str("automation id") }, new[] { "id" }));
            }

            return tools;
        }

        /// <summary>Parse a tool_call.arguments JSON string into a document; tolerates empty/invalid → {}.</summary>
        private static JsonDocument ParseToolArgs(string arguments)
        {
            if (string.IsNullOrWhiteSpace(arguments))
                return JsonDocument.Parse("{}");
            try { return JsonDocument.Parse(arguments); }
            catch { return TryParseJsonObject(arguments) ?? JsonDocument.Parse("{}"); }
        }

        /// <summary>Turn the model's final content into a reply + widgets. Content is normally plain Markdown now
        /// (widgets are attached via the propose_widget tool); we still unwrap the legacy {"final","widgets"} JSON.</summary>
        private static AgentTurnResult FinishAnswer(string content, bool allowWidgets, List<WidgetProposal> pendingWidgets, InsightsReportResult lastDataset)
        {
            var widgets = allowWidgets ? new List<WidgetProposal>(pendingWidgets) : new List<WidgetProposal>();
            var trimmed = content?.Trim() ?? string.Empty;

            var json = ExtractJsonObject(trimmed);
            if (json != null)
            {
                using var doc = TryParseJsonObject(json);
                if (doc != null && doc.RootElement.TryGetProperty("final", out var finalEl))
                {
                    if (allowWidgets && doc.RootElement.TryGetProperty("widgets", out var widgetsEl))
                        widgets.AddRange(BuildWidgets(widgetsEl, lastDataset));
                    return new AgentTurnResult { Reply = finalEl.GetString()?.Trim() ?? string.Empty, Widgets = widgets };
                }
            }
            return new AgentTurnResult { Reply = trimmed, Widgets = widgets };
        }

        /// <summary>Extracts the first balanced JSON object from model output (tolerates ``` fences and stray prose).</summary>
        private static string ExtractJsonObject(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return null;

            var text = content.Trim();
            // Strip code fences.
            if (text.StartsWith("```"))
            {
                var firstNl = text.IndexOf('\n');
                if (firstNl >= 0)
                    text = text[(firstNl + 1)..];
                var fenceEnd = text.LastIndexOf("```", StringComparison.Ordinal);
                if (fenceEnd >= 0)
                    text = text[..fenceEnd];
                text = text.Trim();
            }

            var start = text.IndexOf('{');
            if (start < 0)
                return null;

            var depth = 0;
            var inString = false;
            var escape = false;
            for (var i = start; i < text.Length; i++)
            {
                var c = text[i];
                if (inString)
                {
                    if (escape) escape = false;
                    else if (c == '\\') escape = true;
                    else if (c == '"') inString = false;
                    continue;
                }
                if (c == '"') inString = true;
                else if (c == '{') depth++;
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                        return text.Substring(start, i - start + 1);
                }
            }
            return null;
        }

        /// <summary>Parses the model's JSON envelope, retrying once after escaping raw control chars that
        /// LLMs leave inside Markdown string values. Returns null if it's unparseable even after repair.</summary>
        private static JsonDocument TryParseJsonObject(string json)
        {
            try
            {
                return JsonDocument.Parse(json);
            }
            catch (JsonException)
            {
                try
                {
                    return JsonDocument.Parse(EscapeControlCharsInStrings(json));
                }
                catch (JsonException)
                {
                    return null;
                }
            }
        }

        /// <summary>
        /// Escapes literal newlines/tabs/control chars that appear INSIDE JSON string literals (models
        /// emit Markdown with real newlines, which is invalid JSON). Chars outside strings are untouched.
        /// </summary>
        private static string EscapeControlCharsInStrings(string json)
        {
            if (string.IsNullOrEmpty(json))
                return json;

            var sb = new StringBuilder(json.Length + 32);
            var inString = false;
            var escape = false;
            foreach (var c in json)
            {
                if (inString)
                {
                    if (escape) { sb.Append(c); escape = false; continue; }
                    switch (c)
                    {
                        case '\\': sb.Append(c); escape = true; break;
                        case '"': sb.Append(c); inString = false; break;
                        case '\n': sb.Append("\\n"); break;
                        case '\r': sb.Append("\\r"); break;
                        case '\t': sb.Append("\\t"); break;
                        default:
                            if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                            else sb.Append(c);
                            break;
                    }
                }
                else
                {
                    sb.Append(c);
                    if (c == '"') inString = true;
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Last-ditch recovery when the {"final":"..."} envelope won't parse even after repair (e.g. the
        /// answer itself contains unescaped quotes). Pulls out the answer text so the user still sees it
        /// rather than a misleading error. Returns null if there's no recognizable "final" field.
        /// </summary>
        private static string RecoverFinalText(string json)
        {
            if (string.IsNullOrEmpty(json))
                return null;
            var key = json.IndexOf("\"final\"", StringComparison.Ordinal);
            if (key < 0)
                return null;
            var colon = json.IndexOf(':', key + 7);
            if (colon < 0)
                return null;
            var open = json.IndexOf('"', colon + 1);
            if (open < 0)
                return null;
            // End the value at the last quote before the "widgets" key (or the tail of the object).
            var widgetsAt = json.IndexOf("\"widgets\"", open + 1, StringComparison.Ordinal);
            var searchEnd = widgetsAt >= 0 ? widgetsAt : json.Length;
            var close = json.LastIndexOf('"', searchEnd - 1, searchEnd - 1 - open);
            if (close <= open)
                return null;
            var raw = json.Substring(open + 1, close - open - 1);
            // Turn escaped sequences into their real characters for display (Markdown renders them).
            return raw.Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\t", "\t")
                      .Replace("\\\"", "\"").Replace("\\/", "/").Replace("\\\\", "\\").Trim();
        }

        private static string GetString(JsonElement el, string prop)
        {
            if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String)
                return v.GetString();
            return null;
        }

        private static int? GetInt(JsonElement el, string prop)
        {
            if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(prop, out var v))
                return null;
            if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n))
                return n;
            if (v.ValueKind == JsonValueKind.String &&
                int.TryParse(v.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s))
                return s;
            return null;
        }

        private static bool? GetBool(JsonElement el, string prop)
        {
            if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(prop, out var v))
                return null;
            if (v.ValueKind == JsonValueKind.True) return true;
            if (v.ValueKind == JsonValueKind.False) return false;
            if (v.ValueKind == JsonValueKind.String && bool.TryParse(v.GetString(), out var b)) return b;
            return null;
        }

        private static bool HasProp(JsonElement el, string prop) =>
            el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out _);

        #endregion
    }
}

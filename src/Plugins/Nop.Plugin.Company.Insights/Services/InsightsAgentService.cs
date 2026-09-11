using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
        private static readonly TimeSpan LlmTimeout = TimeSpan.FromSeconds(120);

        private static readonly Dictionary<string, (string Name, string Role, string Focus)> Personas =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["analyst"] = ("Analyst", "a general BI analyst", "orders, revenue, trends and summaries"),
                ["ops"] = ("Operations", "an operations analyst", "order flow, delivery timing and vendor operations"),
                ["finance"] = ("Finance", "a finance analyst", "revenue, reconciliation and invoicing")
            };

        private readonly InsightsLlmClient _llm;
        private readonly IInsightsReportService _reportService;
        private readonly IInsightsMemoryService _memory;
        private readonly ILogger _logger;

        public InsightsAgentService(
            InsightsLlmClient llm,
            IInsightsReportService reportService,
            IInsightsMemoryService memory,
            ILogger logger)
        {
            _llm = llm;
            _reportService = reportService;
            _memory = memory;
            _logger = logger;
        }

        public async Task<AgentTurnResult> RunTurnAsync(ChatTurnRequest request, CancellationToken cancellationToken = default)
        {
            var messages = new List<InsightsLlmClient.LlmMessage>
            {
                new InsightsLlmClient.LlmMessage { Role = "system", Content = BuildSystemPrompt(request.AgentId, _memory.Enabled) }
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

            await InjectMemoryContextAsync(messages, request, cancellationToken);

            InsightsReportResult lastDataset = null;

            try
            {
                for (var i = 0; i < MaxIterations; i++)
                {
                    var content = await _llm.CompleteAsync(
                        InsightsLlmClient.DefaultModel, messages, 0.0, 1200, LlmTimeout, cancellationToken);

                    var json = ExtractJsonObject(content);
                    if (json == null)
                        return new AgentTurnResult { Reply = content.Trim() };

                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("final", out var finalEl))
                    {
                        var widgets = root.TryGetProperty("widgets", out var widgetsEl)
                            ? BuildWidgets(widgetsEl, lastDataset)
                            : new List<WidgetProposal>();
                        return new AgentTurnResult
                        {
                            Reply = finalEl.GetString()?.Trim() ?? string.Empty,
                            Widgets = widgets
                        };
                    }

                    if (root.TryGetProperty("action", out var actionEl))
                    {
                        var tool = actionEl.GetString() ?? "";
                        root.TryGetProperty("args", out var argsEl);
                        var (observation, dataset) = await ExecuteToolAsync(tool, argsEl, request.AgentId, cancellationToken);
                        if (dataset != null)
                            lastDataset = dataset;

                        messages.Add(new InsightsLlmClient.LlmMessage { Role = "assistant", Content = content });
                        messages.Add(new InsightsLlmClient.LlmMessage { Role = "user", Content = $"Observation ({tool}): {observation}" });
                        continue;
                    }

                    // Unrecognised JSON shape — treat its text as the answer.
                    return new AgentTurnResult { Reply = content.Trim() };
                }

                return new AgentTurnResult
                {
                    Reply = "I couldn't finish that within a few steps — try a more specific question."
                };
            }
            catch (Exception ex)
            {
                await _logger.ErrorAsync("Insights agent turn failed", ex);
                return new AgentTurnResult
                {
                    Reply = "The analysis model is warming up (it scales to zero when idle). Please try again in a minute."
                };
            }
        }

        #region Tools

        private async Task<(string observation, InsightsReportResult dataset)> ExecuteToolAsync(
            string tool, JsonElement args, string agentId, CancellationToken ct)
        {
            switch (tool)
            {
                case "remember":
                {
                    var content = GetString(args, "content");
                    if (string.IsNullOrWhiteSpace(content))
                        return ("error: missing 'content'", null);
                    var ok = await _memory.RememberAsync(agentId, GetString(args, "kind"), content, ct);
                    return (ok ? "saved" : "error: memory unavailable", null);
                }
                case "recall":
                {
                    var q = GetString(args, "query");
                    if (string.IsNullOrWhiteSpace(q))
                        return ("error: missing 'query'", null);
                    var mems = await _memory.RecallAsync(agentId, q, 5, ct);
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
                    var result = await _reportService.RunAsync(id, parameters);
                    if (result == null)
                        return ($"error: unknown report id '{id}'", null);
                    return (SummarizeDataset(result), result);
                }
                case "query_orders":
                {
                    var days = GetInt(args, "days") ?? 30;
                    var groupBy = GetString(args, "groupBy") ?? "day";
                    var metric = GetString(args, "metric") ?? "count";
                    var result = await _reportService.QueryOrdersAsync(days, groupBy, metric);
                    return (SummarizeDataset(result), result);
                }
                case "list_reviews":
                {
                    var days = GetInt(args, "days") ?? 30;
                    var vendorId = GetInt(args, "vendorId");
                    var customerId = GetInt(args, "customerId");
                    var customerEmail = GetString(args, "customerEmail");
                    var orderBy = GetString(args, "orderBy") ?? "date";
                    var limit = GetInt(args, "limit");
                    var result = await _reportService.GetReviewsAsync(days, vendorId, customerId, customerEmail, orderBy, limit);
                    return (SummarizeDataset(result), result);
                }
                default:
                    return ($"error: unknown tool '{tool}'", null);
            }
        }

        /// <summary>Auto-recalls relevant saved notes for the latest user message and injects them as context.</summary>
        private async Task InjectMemoryContextAsync(
            List<InsightsLlmClient.LlmMessage> messages, ChatTurnRequest request, CancellationToken ct)
        {
            if (!_memory.Enabled)
                return;

            var lastUser = (request.Messages ?? new List<AgentChatMessage>())
                .LastOrDefault(m => string.Equals(m?.Role, "user", StringComparison.OrdinalIgnoreCase));
            if (lastUser == null || string.IsNullOrWhiteSpace(lastUser.Content))
                return;

            var memories = await _memory.RecallAsync(request.AgentId, lastUser.Content, 3, ct);
            if (memories.Count == 0)
                return;

            var sb = new StringBuilder("Notes you saved in earlier conversations (use if relevant, ignore otherwise):\n");
            foreach (var m in memories)
                sb.AppendLine("- " + m.Content);

            // Insert just after the persona system prompt.
            messages.Insert(1, new InsightsLlmClient.LlmMessage { Role = "system", Content = sb.ToString() });
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
                var type = GetString(w, "kind") ?? "chart";
                list.Add(new WidgetProposal
                {
                    Type = type == "table" ? "table" : "chart",
                    Title = GetString(w, "title") ?? "Result",
                    ChartKind = GetString(w, "chartKind") ?? "line",
                    XField = GetString(w, "xField"),
                    YField = GetString(w, "yField"),
                    CategoryField = GetString(w, "categoryField"),
                    Columns = dataset.Columns,
                    Rows = dataset.Rows
                });
            }
            return list;
        }

        #endregion

        #region Prompt & JSON helpers

        private static string BuildSystemPrompt(string agentId, bool memoryEnabled)
        {
            var persona = Personas.TryGetValue(agentId ?? "analyst", out var p) ? p : Personas["analyst"];

            var sb = new StringBuilder();
            sb.AppendLine($"You are {persona.Name}, {persona.Role} for the MySnacks food-ordering platform, focused on {persona.Focus}.");
            sb.AppendLine("You help backoffice staff explore this tenant's order data. You are READ-ONLY: you can only read data through the tools below, never modify anything.");
            if (memoryEnabled)
                sb.AppendLine("You have long-term memory across conversations: consult it with recall, and save durable, reusable learnings (not one-off facts) with remember.");
            sb.AppendLine();
            sb.AppendLine("Respond with EXACTLY ONE JSON object per turn and nothing else — no markdown, no text outside the JSON. Use one of two shapes:");
            sb.AppendLine("1) Call a tool: {\"action\":\"<tool>\",\"args\":{...}}");
            sb.AppendLine("2) Finish: {\"final\":\"<concise answer citing real figures>\",\"widgets\":[<widget>...]}");
            sb.AppendLine();
            sb.AppendLine("Tools:");
            sb.AppendLine("- list_reports {}  -> available named reports.");
            sb.AppendLine("- run_report {\"id\":\"<reportId>\",\"days\":<int, optional>}  -> a report's columns and rows. list_reports shows each report's parameters and their min/max; \"days\" is clamped to the report's allowed range (e.g. up to 90).");
            sb.AppendLine("- query_orders {\"days\":<int, optional, default 30, max 365>,\"groupBy\":\"day\"|\"status\",\"metric\":\"count\"|\"revenue\"}  -> aggregated orders over the last N days.");
            sb.AppendLine("- list_reviews {\"days\":<int, optional, default 30, max 90>,\"vendorId\":<int, optional>,\"customerId\":<int, optional>,\"customerEmail\":\"...\"(optional),\"orderBy\":\"date\"|\"rating\"|\"helpful\"(optional),\"limit\":<int, optional, default 50, max 200>}  -> product reviews (date, product, vendor, customer name+email, rating, approved, title, review, and triage: who/when/hours/resolution).");
            if (memoryEnabled)
            {
                sb.AppendLine("- recall {\"query\":\"...\"}  -> retrieve notes you saved in earlier conversations.");
                sb.AppendLine("- remember {\"content\":\"...\",\"kind\":\"note\"}  -> save a durable, reusable learning for future conversations.");
            }
            sb.AppendLine();
            sb.AppendLine("Widget (optional; visualizes the MOST RECENT dataset you fetched this turn):");
            sb.AppendLine("{\"kind\":\"chart\"|\"table\",\"chartKind\":\"line\"|\"area\"|\"bar\"|\"pie\",\"title\":\"...\",\"xField\":\"<column>\",\"yField\":\"<column>\",\"categoryField\":\"<column>\"}");
            sb.AppendLine();
            sb.AppendLine("Rules: always fetch real data with a tool before answering; never invent numbers. Field names in widgets must match the dataset columns. Keep answers short. Only add widgets when a chart or table genuinely helps.");
            return sb.ToString();
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

        #endregion
    }
}

using System;

namespace Nop.Plugin.Company.Insights.Models
{
    /// <summary>
    /// A background-agent configuration (the thing WM/admin creates, usually via the main chat).
    /// Stored in the separate Postgres. See docs/BACKGROUND-AGENTS.md §4.
    /// </summary>
    public class InsightsAgentConfig
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public bool Enabled { get; set; } = true;
        public bool BuiltIn { get; set; }
        public int? OwnerUserId { get; set; }

        /// <summary>Scope: restrict to one company (null = global / all companies).</summary>
        public int? CompanyId { get; set; }

        /// <summary>"event" | "schedule".</summary>
        public string TriggerKind { get; set; }
        /// <summary>Event type from the registry (for TriggerKind == "event").</summary>
        public string EventType { get; set; }
        /// <summary>5-field cron, Asia/Yerevan (for TriggerKind == "schedule").</summary>
        public string Cron { get; set; }

        /// <summary>Optional predicate JSON that narrows which events fire this agent, e.g. {"minRating":2}.</summary>
        public string FilterJson { get; set; }

        /// <summary>The background agent's system prompt (written by the main agent) + task instruction.</summary>
        public string SystemPrompt { get; set; }
        public string Instruction { get; set; }

        /// <summary>Output sinks JSON array, e.g. ["telegram","memory","dashboard"]. + a target (telegram chat id).</summary>
        public string OutputSinksJson { get; set; }
        public string OutputTarget { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>One execution of an agent (the run ledger / observability). See §9.</summary>
    public class InsightsAgentRun
    {
        public long Id { get; set; }
        public string AgentId { get; set; }
        public string AgentName { get; set; }
        public long? EventId { get; set; }
        public string TriggerType { get; set; }
        public string InputJson { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime? FinishedAt { get; set; }
        public int? DurationMs { get; set; }
        public string Status { get; set; }     // running | ok | error | skipped
        public string OutputJson { get; set; }
        public string Error { get; set; }
    }
}

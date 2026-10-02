using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Nop.Services.Ai
{
    /// <summary>
    /// The single, shared chat-completions client for the self-hosted KubeAI/vLLM gateway
    /// (OpenAI-compatible). Every feature that talks to the model - support-case subject
    /// generation, RemindMe's meal recommendation, the Insights BI agent's tool-calling loop -
    /// goes through this one client; only the model id, prompts, and (for Insights) tool schemas
    /// differ per use case. Each caller picks its own model, typically via its own env-var
    /// override (same convention as before: e.g. REMINDME_LLM_MODEL, SUPPORT_SUBJECT_LLM_MODEL,
    /// INSIGHTS_LLM_MODEL) rather than this client hard-coding one.
    /// </summary>
    public interface IKubeAiChatClient
    {
        /// <summary>
        /// Convenience wrapper for a single-turn "system prompt + user prompt -&gt; text" completion
        /// (RemindMe's recommendation, support-case subject generation). For a real multi-turn or
        /// tool-calling conversation, use <see cref="CompleteAsync"/>/<see cref="CompleteWithToolsAsync"/>.
        /// Throws on any HTTP error, timeout, or missing content - callers are expected to catch and
        /// fall back (the model is scale-to-zero, so a cold start is the common "failure", not the
        /// exception).
        /// </summary>
        Task<string> GetChatCompletionAsync(string model, string systemPrompt, string userPrompt,
            TimeSpan timeout, CancellationToken cancellationToken = default, int? maxTokens = null);

        /// <summary>
        /// Posts a chat completion over a full message transcript and returns the assistant content.
        /// Pass maxTokens=null to leave the completion uncapped so a reasoning model can finish
        /// thinking AND answer - a cap that truncates the think block yields empty content. Throws on
        /// HTTP error/timeout/empty content.
        /// </summary>
        Task<string> CompleteAsync(string model, IEnumerable<LlmMessage> messages, double temperature,
            int? maxTokens, TimeSpan timeout, CancellationToken cancellationToken = default);

        /// <summary>
        /// Posts a chat completion WITH native tool-calling (requires a vLLM gateway configured with
        /// --enable-auto-tool-choice). Returns the model's answer content and/or the tools it wants to
        /// call. The caller runs the tools, appends the results as role:"tool" messages, and calls
        /// again until the model answers with content.
        /// </summary>
        Task<LlmCompletion> CompleteWithToolsAsync(string model, IEnumerable<LlmMessage> messages,
            double temperature, IEnumerable<LlmTool> tools, TimeSpan timeout,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Cheap readiness probe (a minimal 1-token completion). Never throws - returns false on any
        /// failure, including the model still being scaled to zero / mid cold-start.
        /// </summary>
        Task<bool> IsReadyAsync(string model, TimeSpan timeout, CancellationToken cancellationToken = default);
    }
}

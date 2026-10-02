using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Nop.Services.Ai
{
    /// <summary>
    /// One turn in a chat-completion conversation. Shared across every KubeAI consumer (a single
    /// system+user pair for a one-shot completion, or a real multi-turn/tool-calling transcript).
    /// </summary>
    public class LlmMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; }

        [JsonPropertyName("content")]
        public string Content { get; set; }

        // Assistant messages that call tools carry the calls; echoing them back (with the tool results
        // as role:"tool" messages) is required by the OpenAI/vLLM tool-calling protocol.
        [JsonPropertyName("tool_calls")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<LlmToolCall> ToolCalls { get; set; }

        // role:"tool" result plumbing.
        [JsonPropertyName("tool_call_id")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string ToolCallId { get; set; }

        [JsonPropertyName("name")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Name { get; set; }
    }

    public class LlmToolCall
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; } = "function";

        [JsonPropertyName("function")]
        public LlmFunctionCall Function { get; set; }
    }

    public class LlmFunctionCall
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        /// <summary>Arguments as a JSON string (per the OpenAI schema), e.g. <c>{"days":7}</c>.</summary>
        [JsonPropertyName("arguments")]
        public string Arguments { get; set; }
    }

    /// <summary>A tool the model may call. <see cref="LlmFunctionDef.Parameters"/> is a JSON-schema object.</summary>
    public class LlmTool
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "function";

        [JsonPropertyName("function")]
        public LlmFunctionDef Function { get; set; }
    }

    public class LlmFunctionDef
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; }

        [JsonPropertyName("parameters")]
        public object Parameters { get; set; }
    }

    /// <summary>Result of a tool-enabled completion: the model either called tools OR returned an answer.</summary>
    public class LlmCompletion
    {
        public string Content { get; set; }
        public IList<LlmToolCall> ToolCalls { get; set; }
        public bool HasToolCalls => ToolCalls != null && ToolCalls.Count > 0;
    }
}

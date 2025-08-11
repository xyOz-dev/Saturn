namespace Saturn.OpenRouter.Models.Api.Chat
{
    /// <summary>
    /// Represents a chunk of streaming output from the model, optionally containing a tool call delta.
    /// </summary>
    public sealed class StreamChunk
    {
        /// <summary>Incrementing index for streamed tokens/chunks.</summary>
        public int TokenIndex { get; set; }

        /// <summary>True when this is the final chunk for the current response.</summary>
        public bool IsComplete { get; set; }

        /// <summary>Text content delta when not a tool call.</summary>
        public string? Content { get; set; }

        /// <summary>Assistant role (usually "assistant").</summary>
        public string? Role { get; set; }

        /// <summary>Whether this chunk represents a tool call delta.</summary>
        public bool IsToolCall { get; set; }

        /// <summary>ID of the tool call (from the provider).</summary>
        public string? ToolCallId { get; set; }

        /// <summary>Name of the tool being called.</summary>
        public string? ToolName { get; set; }

        /// <summary>Arguments for the tool call, as a stringified JSON delta.</summary>
        public string? ToolArguments { get; set; }
    }
}
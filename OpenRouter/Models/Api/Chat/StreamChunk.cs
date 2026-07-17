namespace Saturn.OpenRouter.Models.Api.Chat
{
    public sealed class StreamChunk
    {
        public int TokenIndex { get; set; }

        public bool IsComplete { get; set; }

        // Signals the consumer to discard any partial output streamed so far for the
        // current response (e.g. when a retry is about to re-send the turn).
        public bool ResetContent { get; set; }

        // Status text for live display only (e.g. "waiting 2s before retrying");
        // consumers must not persist it as part of the assistant's message.
        public bool IsTransientNotice { get; set; }

        public string? Content { get; set; }

        public string? Role { get; set; }

        public bool IsToolCall { get; set; }

        public string? ToolCallId { get; set; }

        public string? ToolName { get; set; }

        public string? ToolArguments { get; set; }
    }
}
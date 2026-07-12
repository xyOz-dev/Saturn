namespace Saturn.OpenRouter.Models.Api.Chat
{
    public sealed class StreamChunk
    {
        public int TokenIndex { get; set; }

        public bool IsComplete { get; set; }

        public string? Content { get; set; }

        public string? Role { get; set; }

        public bool IsToolCall { get; set; }

        public string? ToolCallId { get; set; }

        public string? ToolName { get; set; }

        public string? ToolArguments { get; set; }
    }
}
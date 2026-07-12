namespace Saturn.OpenRouter.Http
{
    public sealed class SseEvent
    {
        public string? Event { get; set; }

        public string? Data { get; set; }

        public bool IsComment { get; set; }
    }
}
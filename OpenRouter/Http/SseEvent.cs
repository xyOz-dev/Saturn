namespace Saturn.OpenRouter.Http
{
    /// <summary>
    /// Represents a single Server-Sent Events (SSE) line parsed from the stream.
    /// For this milestone, only "data: ..." lines are yielded as events and comment lines (": ...") are ignored.
    /// </summary>
    public sealed class SseEvent
    {
        /// <summary>Optional event name if an "event: ..." field was encountered.</summary>
        public string? Event { get; set; }

        /// <summary>The payload of a "data: ..." line, if any.</summary>
        public string? Data { get; set; }

        /// <summary>True if this represents a comment line (": ...").</summary>
        public bool IsComment { get; set; }
    }
}
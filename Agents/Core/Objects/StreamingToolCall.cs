using Saturn.OpenRouter.Services;

namespace Saturn.Agents.Core.Objects
{
    internal class StreamingToolCall
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public JsonStreamAccumulator ArgumentsAccumulator { get; } = new JsonStreamAccumulator();
        public bool IsComplete => ArgumentsAccumulator.IsComplete;
    }
}
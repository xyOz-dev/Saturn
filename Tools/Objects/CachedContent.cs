using System;
using Saturn.Tools.Core;

namespace Saturn.Tools.Objects
{
    internal class CachedContent
    {
        public ToolResult Result { get; set; } = new ToolResult { FormattedOutput = string.Empty, RawData = string.Empty };
        public DateTime CachedAt { get; set; }
    }
}
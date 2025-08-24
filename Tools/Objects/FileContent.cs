using System.Collections.Generic;

namespace Saturn.Tools.Objects
{
    internal class FileContent
    {
        public List<string> Lines { get; set; } = new List<string>();
        public int TotalLines { get; set; }
        public int ReadLines { get; set; }
    }
}
using System.Collections.Generic;
using System.Text;

namespace Saturn.UI.Objects
{
    public class FormattedText
    {
        private List<(string text, Terminal.Gui.Attribute attribute)> segments = new();

        public void Add(string text, Terminal.Gui.Attribute attribute)
        {
            segments.Add((text, attribute));
        }

        public List<(string text, Terminal.Gui.Attribute attribute)> GetSegments()
        {
            return segments;
        }

        public void Clear()
        {
            segments.Clear();
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            foreach (var segment in segments)
            {
                sb.Append(segment.text);
            }
            return sb.ToString();
        }
    }
}
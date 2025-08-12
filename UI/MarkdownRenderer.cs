using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Terminal.Gui;

namespace Saturn.UI
{
    public class MarkdownRenderer
    {
        private readonly Dictionary<string, Color> codeBlockColors = new()
        {
            { "keyword", Color.BrightMagenta },
            { "string", Color.BrightGreen },
            { "comment", Color.DarkGray },
            { "default", Color.Gray }
        };

        public string RenderToTerminal(string markdown)
        {
            if (string.IsNullOrEmpty(markdown))
                return string.Empty;

            var result = new StringBuilder();
            var lines = markdown.Split('\n');
            bool inCodeBlock = false;
            string codeLanguage = "";

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];

                if (line.StartsWith("```"))
                {
                    if (!inCodeBlock)
                    {
                        inCodeBlock = true;
                        codeLanguage = line.Length > 3 ? line.Substring(3).Trim() : "";
                        result.AppendLine($"┌─[{(string.IsNullOrEmpty(codeLanguage) ? "code" : codeLanguage)}]─────────────────────────────────────");
                    }
                    else
                    {
                        inCodeBlock = false;
                        codeLanguage = "";
                        result.AppendLine("└──────────────────────────────────────────────────");
                    }
                    continue;
                }

                if (inCodeBlock)
                {
                    result.AppendLine($"│ {line}");
                }
                else
                {
                    line = RenderInlineElements(line);
                    line = RenderHeaders(line);
                    line = RenderLists(line);
                    line = RenderHorizontalRule(line);
                    line = RenderBlockquote(line);
                    result.AppendLine(line);
                }
            }

            return result.ToString();
        }

        private string RenderInlineElements(string text)
        {
            text = Regex.Replace(text, @"\*\*(.+?)\*\*", "►$1◄");
            
            text = Regex.Replace(text, @"\*(.+?)\*", "$1");
            text = Regex.Replace(text, @"_(.+?)_", "$1");
            
            text = Regex.Replace(text, @"`([^`]+)`", "[$1]");
            
            text = Regex.Replace(text, @"\[([^\]]+)\]\(([^)]+)\)", "$1 ($2)");

            return text;
        }

        private string RenderHeaders(string line)
        {
            if (line.StartsWith("# "))
                return $"═══ {line.Substring(2).ToUpper()} ═══";
            if (line.StartsWith("## "))
                return $"══ {line.Substring(3)} ══";
            if (line.StartsWith("### "))
                return $"═ {line.Substring(4)} ═";
            if (line.StartsWith("#### "))
                return $"▪ {line.Substring(5)}";
            
            return line;
        }

        private string RenderLists(string line)
        {
            line = Regex.Replace(line, @"^(\s*)\* (.+)", "$1• $2");
            line = Regex.Replace(line, @"^(\s*)- (.+)", "$1• $2");
            line = Regex.Replace(line, @"^(\s*)\+ (.+)", "$1• $2");
            
            line = Regex.Replace(line, @"^(\s*)(\d+)\. (.+)", "$1$2. $3");
            
            return line;
        }

        private string RenderHorizontalRule(string line)
        {
            if (line.Trim() == "---" || line.Trim() == "***" || line.Trim() == "___")
                return "─────────────────────────────────────────────────────";
            
            return line;
        }

        private string RenderBlockquote(string line)
        {
            if (line.StartsWith("> "))
                return $"│ {line.Substring(2)}";
            
            return line;
        }

        public static string StripMarkdown(string markdown)
        {
            if (string.IsNullOrEmpty(markdown))
                return string.Empty;

            var text = markdown;
            
            text = Regex.Replace(text, @"```[\s\S]*?```", "");
            
            text = Regex.Replace(text, @"`([^`]+)`", "$1");
            
            text = Regex.Replace(text, @"\*\*(.+?)\*\*", "$1");
            text = Regex.Replace(text, @"\*(.+?)\*", "$1");
            text = Regex.Replace(text, @"_(.+?)_", "$1");
            
            text = Regex.Replace(text, @"#+\s*", "");
            
            text = Regex.Replace(text, @"\[([^\]]+)\]\([^)]+\)", "$1");
            
            text = Regex.Replace(text, @"^[\*\-\+]\s+", "", RegexOptions.Multiline);
            text = Regex.Replace(text, @"^\d+\.\s+", "", RegexOptions.Multiline);
            
            text = Regex.Replace(text, @"^>\s+", "", RegexOptions.Multiline);
            
            text = Regex.Replace(text, @"^(---|\*\*\*|___)$", "", RegexOptions.Multiline);

            return text;
        }

        public FormattedText RenderToFormattedText(string markdown)
        {
            var rendered = RenderToTerminal(markdown);
            var formattedText = new FormattedText();
            
            var lines = rendered.Split('\n');
            foreach (var line in lines)
            {
                if (line.StartsWith("│ ") && line.Contains("└") == false && line.Contains("┌") == false)
                {
                    formattedText.Add(line, new Terminal.Gui.Attribute(Color.Cyan, Color.Black));
                }
                else if (line.Contains("═══") || line.Contains("══") || line.Contains("═ "))
                {
                    formattedText.Add(line, new Terminal.Gui.Attribute(Color.BrightYellow, Color.Black));
                }
                else if (line.Contains("►") && line.Contains("◄"))
                {
                    var parts = line.Split(new[] { '►', '◄' }, StringSplitOptions.None);
                    for (int i = 0; i < parts.Length; i++)
                    {
                        if (i % 2 == 1)
                            formattedText.Add(parts[i], new Terminal.Gui.Attribute(Color.White, Color.Black));
                        else
                            formattedText.Add(parts[i], new Terminal.Gui.Attribute(Color.Gray, Color.Black));
                    }
                }
                else
                {
                    formattedText.Add(line, new Terminal.Gui.Attribute(Color.Gray, Color.Black));
                }
                
                if (lines.Length > 1)
                    formattedText.Add("\n", new Terminal.Gui.Attribute(Color.Gray, Color.Black));
            }
            
            return formattedText;
        }
    }

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
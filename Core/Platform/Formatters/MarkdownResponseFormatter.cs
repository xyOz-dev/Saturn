using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Saturn.OpenRouter.Models.Api.Chat;

namespace Saturn.Core.Platform.Formatters
{
    public class MarkdownResponseFormatter : IResponseFormatter
    {
        public string FormatName => "Markdown";
        
        public Task<FormattedResponse> FormatMessageAsync(Message message, FormattingContext context)
        {
            var content = message?.Content.ValueKind != JsonValueKind.Undefined 
                ? message.Content.ToString() 
                : string.Empty;
            
            if (context.PreferPlainText)
            {
                content = RemoveMarkdown(content);
            }
            else if (!context.SupportsMarkdown)
            {
                content = ConvertMarkdownToPlainText(content);
            }
            
            if (content.Length > context.MaxLength)
            {
                return Task.FromResult(new FormattedResponse
                {
                    Content = TruncateMessage(content, context.MaxLength),
                    Format = context.SupportsMarkdown ? ResponseFormat.Markdown : ResponseFormat.PlainText,
                    RequiresSplit = true
                });
            }
            
            return Task.FromResult(new FormattedResponse
            {
                Content = content,
                Format = context.SupportsMarkdown ? ResponseFormat.Markdown : ResponseFormat.PlainText
            });
        }
        
        public Task<FormattedResponse> FormatErrorAsync(string error, ErrorLevel level, FormattingContext context)
        {
            var emoji = level switch
            {
                ErrorLevel.Info => "ℹ️",
                ErrorLevel.Warning => "⚠️",
                ErrorLevel.Error => "❌",
                ErrorLevel.Critical => "🚨",
                _ => "❓"
            };
            
            var color = level switch
            {
                ErrorLevel.Info => 0x3498db,
                ErrorLevel.Warning => 0xf39c12,
                ErrorLevel.Error => 0xe74c3c,
                ErrorLevel.Critical => 0x992d22,
                _ => 0x95a5a6
            };
            
            var formattedError = context.SupportsMarkdown
                ? $"{emoji} **{level}**: {error}"
                : $"{emoji} {level}: {error}";
            
            if (context.SupportsEmbeds)
            {
                return Task.FromResult(new FormattedResponse
                {
                    Format = ResponseFormat.Embed,
                    Embed = new PlatformEmbed
                    {
                        Title = $"{emoji} {level}",
                        Description = error,
                        Color = color
                    }
                });
            }
            
            return Task.FromResult(new FormattedResponse
            {
                Content = formattedError,
                Format = context.SupportsMarkdown ? ResponseFormat.Markdown : ResponseFormat.PlainText,
                Color = color
            });
        }
        
        public Task<FormattedResponse> FormatToolResultAsync(string toolName, object result, FormattingContext context)
        {
            var resultString = result?.ToString() ?? "No result";
            
            if (context.SupportsCodeBlocks)
            {
                var formatted = $"**Tool:** `{toolName}`\n```\n{resultString}\n```";
                
                return Task.FromResult(new FormattedResponse
                {
                    Content = formatted,
                    Format = ResponseFormat.Markdown
                });
            }
            
            var plainFormatted = $"Tool: {toolName}\n{resultString}";
            
            return Task.FromResult(new FormattedResponse
            {
                Content = plainFormatted,
                Format = ResponseFormat.PlainText
            });
        }
        
        public Task<FormattedResponse> FormatCodeBlockAsync(string code, string? language, FormattingContext context)
        {
            if (!context.SupportsCodeBlocks)
            {
                return Task.FromResult(new FormattedResponse
                {
                    Content = code,
                    Format = ResponseFormat.PlainText
                });
            }
            
            var formatted = string.IsNullOrEmpty(language)
                ? $"```\n{code}\n```"
                : $"```{language}\n{code}\n```";
            
            return Task.FromResult(new FormattedResponse
            {
                Content = formatted,
                Format = ResponseFormat.Code
            });
        }
        
        public async Task<List<FormattedResponse>> SplitLongMessageAsync(string content, FormattingContext context)
        {
            var responses = new List<FormattedResponse>();
            var maxLength = context.MaxLength - 50; // Reserve space for formatting
            
            var chunks = SplitByCodeBlocks(content, maxLength);
            
            foreach (var chunk in chunks)
            {
                var formatted = await FormatMessageAsync(new Message 
                { 
                    Content = JsonDocument.Parse(JsonSerializer.Serialize(chunk)).RootElement 
                }, context);
                
                responses.Add(formatted);
            }
            
            return responses;
        }
        
        public string TruncateMessage(string content, int maxLength, string suffix = "...")
        {
            if (content.Length <= maxLength)
                return content;
            
            var truncateAt = maxLength - suffix.Length;
            
            var lastSpace = content.LastIndexOf(' ', truncateAt);
            if (lastSpace > truncateAt * 0.8)
            {
                truncateAt = lastSpace;
            }
            
            return content.Substring(0, truncateAt) + suffix;
        }
        
        public string SanitizeContent(string content)
        {
            content = Regex.Replace(content, @"@(everyone|here)\b", "@\u200b$1");
            
            content = Regex.Replace(content, @"<@!?\d+>", match => match.Value.Insert(2, "\u200b"));
            
            // Code blocks are preserved as-is during sanitization
            
            return content;
        }
        
        private string RemoveMarkdown(string content)
        {
            content = Regex.Replace(content, @"\*\*(.*?)\*\*", "$1");
            content = Regex.Replace(content, @"\*(.*?)\*", "$1");
            content = Regex.Replace(content, @"__(.*?)__", "$1");
            content = Regex.Replace(content, @"_(.*?)_", "$1");
            content = Regex.Replace(content, @"~~(.*?)~~", "$1");
            content = Regex.Replace(content, @"`(.*?)`", "$1");
            content = Regex.Replace(content, @"```[\s\S]*?```", match =>
            {
                var code = match.Value;
                code = Regex.Replace(code, @"```\w*\n?", "");
                return code;
            });
            content = Regex.Replace(content, @"^\s*#+\s+", "", RegexOptions.Multiline);
            content = Regex.Replace(content, @"\[([^\]]+)\]\([^\)]+\)", "$1");
            content = Regex.Replace(content, @"^\s*[-*+]\s+", "• ", RegexOptions.Multiline);
            content = Regex.Replace(content, @"^\s*\d+\.\s+", "", RegexOptions.Multiline);
            content = Regex.Replace(content, @"^\s*>+\s+", "", RegexOptions.Multiline);
            
            return content.Trim();
        }
        
        private string ConvertMarkdownToPlainText(string content)
        {
            content = Regex.Replace(content, @"\*\*(.*?)\*\*", match => 
                match.Groups[1].Value.ToUpper());
            
            content = RemoveMarkdown(content);
            
            return content;
        }
        
        private List<string> SplitByCodeBlocks(string content, int maxLength)
        {
            var chunks = new List<string>();
            var codeBlockPattern = @"```[\s\S]*?```";
            var matches = Regex.Matches(content, codeBlockPattern);
            
            if (matches.Count == 0)
            {
                return SplitByLength(content, maxLength);
            }
            
            var lastIndex = 0;
            foreach (Match match in matches)
            {
                if (match.Index > lastIndex)
                {
                    var textBefore = content.Substring(lastIndex, match.Index - lastIndex);
                    chunks.AddRange(SplitByLength(textBefore, maxLength));
                }
                
                if (match.Length <= maxLength)
                {
                    chunks.Add(match.Value);
                }
                else
                {
                    chunks.AddRange(SplitCodeBlock(match.Value, maxLength));
                }
                
                lastIndex = match.Index + match.Length;
            }
            
            if (lastIndex < content.Length)
            {
                var remaining = content.Substring(lastIndex);
                chunks.AddRange(SplitByLength(remaining, maxLength));
            }
            
            return chunks;
        }
        
        private List<string> SplitByLength(string content, int maxLength)
        {
            var chunks = new List<string>();
            var lines = content.Split('\n');
            var currentChunk = new StringBuilder();
            
            foreach (var line in lines)
            {
                if (currentChunk.Length + line.Length + 1 > maxLength)
                {
                    if (currentChunk.Length > 0)
                    {
                        chunks.Add(currentChunk.ToString().Trim());
                        currentChunk.Clear();
                    }
                    
                    if (line.Length > maxLength)
                    {
                        var words = line.Split(' ');
                        foreach (var word in words)
                        {
                            if (currentChunk.Length + word.Length + 1 > maxLength)
                            {
                                chunks.Add(currentChunk.ToString().Trim());
                                currentChunk.Clear();
                            }
                            
                            if (currentChunk.Length > 0)
                                currentChunk.Append(' ');
                            currentChunk.Append(word);
                        }
                    }
                    else
                    {
                        currentChunk.AppendLine(line);
                    }
                }
                else
                {
                    currentChunk.AppendLine(line);
                }
            }
            
            if (currentChunk.Length > 0)
            {
                chunks.Add(currentChunk.ToString().Trim());
            }
            
            return chunks;
        }
        
        private List<string> SplitCodeBlock(string codeBlock, int maxLength)
        {
            var chunks = new List<string>();
            var lines = codeBlock.Split('\n');
            var language = "";
            
            if (lines[0].StartsWith("```"))
            {
                language = lines[0].Substring(3);
                lines = lines.Skip(1).ToArray();
            }
            
            if (lines[lines.Length - 1] == "```")
            {
                lines = lines.Take(lines.Length - 1).ToArray();
            }
            
            var currentChunk = new StringBuilder($"```{language}\n");
            
            foreach (var line in lines)
            {
                if (currentChunk.Length + line.Length + 4 > maxLength) // +4 for closing ```
                {
                    currentChunk.AppendLine("```");
                    chunks.Add(currentChunk.ToString());
                    currentChunk = new StringBuilder($"```{language}\n");
                }
                
                currentChunk.AppendLine(line);
            }
            
            currentChunk.AppendLine("```");
            chunks.Add(currentChunk.ToString());
            
            return chunks;
        }
    }
}
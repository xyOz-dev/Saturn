using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Saturn.Tools.Core;

namespace Saturn.Tools
{
    public class ReadFileTool : ToolBase
    {
        public override string Name => "read_file";
        
        public override string Description => @"Use this tool to read file contents. This is essential before editing files or when you need to understand code structure and implementation details.

When to use:
- ALWAYS before using apply_diff to edit a file
- When examining code implementation details
- To understand file structure and content
- When debugging or analyzing specific code sections
- To read configuration files, documentation, or data files

How to use:
- Set 'filePath' to the file you want to read
- Use 'startLine' and 'endLine' for reading specific sections of large files
- Default reads entire file if no line range specified
- Line numbers are shown with each line for easy reference

Important rules:
- You MUST read a file before attempting to edit it with apply_diff
- For very large files, use line ranges to read relevant sections
- The tool shows line numbers which you'll need for apply_diff context

Examples:
- Read entire file: filePath='src/UserService.cs'
- Read lines 50-100: filePath='src/UserService.cs', startLine=50, endLine=100
- Read from line 200 onward: filePath='src/UserService.cs', startLine=200";
        
        protected override Dictionary<string, object> GetParameterProperties()
        {
            return new Dictionary<string, object>
            {
                ["path"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["description"] = "File path to read. Must be an absolute or relative path to an existing file"
                },
                ["startLine"] = new Dictionary<string, object>
                {
                    ["type"] = "integer",
                    ["description"] = "Starting line number to read from (1-based). Default reads from beginning"
                },
                ["endLine"] = new Dictionary<string, object>
                {
                    ["type"] = "integer",
                    ["description"] = "Ending line number to read to (inclusive). Default reads to end of file"
                },
                ["encoding"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["description"] = "Text encoding to use. Options: utf8, utf16, utf32, ascii, unicode. Default is utf8"
                },
                ["includeLineNumbers"] = new Dictionary<string, object>
                {
                    ["type"] = "boolean",
                    ["description"] = "Include line numbers in output. Default is true"
                },
                ["includeMetadata"] = new Dictionary<string, object>
                {
                    ["type"] = "boolean",
                    ["description"] = "Include file metadata like size, dates, and encoding. Default is true"
                }
            };
        }
        
        protected override string[] GetRequiredParameters()
        {
            return new[] { "path" };
        }
        
        public override string GetDisplaySummary(Dictionary<string, object> parameters)
        {
            var path = GetParameter<string>(parameters, "path", "");
            var startLine = GetParameter<int?>(parameters, "startLine", null);
            var endLine = GetParameter<int?>(parameters, "endLine", null);
            
            var filename = string.IsNullOrEmpty(path) ? "unknown" : System.IO.Path.GetFileName(path);
            
            if (startLine.HasValue && endLine.HasValue)
            {
                return $"Reading {filename} [{startLine}-{endLine}]";
            }
            else if (startLine.HasValue)
            {
                return $"Reading {filename} [from line {startLine}]";
            }
            else if (endLine.HasValue)
            {
                return $"Reading {filename} [up to line {endLine}]";
            }
            else
            {
                return $"Reading {filename} (entire file)";
            }
        }
        
        public override async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters)
        {
            var path = GetParameter<string>(parameters, "path");
            var startLine = GetParameter<int?>(parameters, "startLine", null);
            var endLine = GetParameter<int?>(parameters, "endLine", null);
            var encodingName = GetParameter<string>(parameters, "encoding", "utf8");
            var includeLineNumbers = GetParameter<bool>(parameters, "includeLineNumbers", true);
            var includeMetadata = GetParameter<bool>(parameters, "includeMetadata", true);
            
            if (string.IsNullOrEmpty(path))
            {
                return CreateErrorResult("Path CANNOT be empty");
            }
            
            if (!File.Exists(path))
            {
                return CreateErrorResult($"File NOT found: {path}");
            }
            
            try
            {
                var encoding = GetEncoding(encodingName);
                var fileInfo = new FileInfo(path);
                var result = await ReadFileContent(path, encoding, startLine, endLine, includeLineNumbers);
                
                return FormatResults(result, fileInfo, encoding, includeMetadata);
            }
            catch (UnauthorizedAccessException)
            {
                return CreateErrorResult($"Access denied: {path}");
            }
            catch (IOException ex)
            {
                return CreateErrorResult($"IO error reading file: {ex.Message}");
            }
            catch (Exception ex)
            {
                return CreateErrorResult($"Error reading file: {ex.Message}");
            }
        }
        
        private async Task<FileContent> ReadFileContent(string path, Encoding encoding, int? startLine, int? endLine, bool includeLineNumbers)
        {
            var content = new FileContent
            {
                Lines = new List<string>(),
                TotalLines = 0,
                ReadLines = 0
            };
            
            await Task.Run(() =>
            {
                using (var reader = new StreamReader(path, encoding))
                {
                    string line;
                    int lineNumber = 0;
                    
                    while ((line = reader.ReadLine()) != null)
                    {
                        lineNumber++;
                        content.TotalLines++;
                        
                        if (startLine.HasValue && lineNumber < startLine.Value)
                            continue;
                            
                        if (endLine.HasValue && lineNumber > endLine.Value)
                            break;
                        
                        if (includeLineNumbers)
                        {
                            content.Lines.Add($"{lineNumber,6}: {line}");
                        }
                        else
                        {
                            content.Lines.Add(line);
                        }
                        
                        content.ReadLines++;
                    }
                }
            });
            
            return content;
        }
        
        private ToolResult FormatResults(FileContent content, FileInfo fileInfo, Encoding encoding, bool includeMetadata)
        {
            var lines = new List<string>();
            
            if (includeMetadata)
            {
                lines.Add($"File: {fileInfo.FullName}");
                lines.Add($"Size: {FormatFileSize(fileInfo.Length)}");
                lines.Add($"Encoding: {encoding.EncodingName}");
                lines.Add($"Created: {fileInfo.CreationTime:yyyy-MM-dd HH:mm:ss}");
                lines.Add($"Modified: {fileInfo.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
                lines.Add($"Lines: {content.ReadLines} of {content.TotalLines} total");
                lines.Add("");
            }
            
            lines.AddRange(content.Lines);
            
            var result = new
            {
                FilePath = fileInfo.FullName,
                FileSize = fileInfo.Length,
                Encoding = encoding.EncodingName,
                TotalLines = content.TotalLines,
                LinesRead = content.ReadLines,
                Content = content.Lines
            };
            
            return CreateSuccessResult(result, string.Join(Environment.NewLine, lines));
        }
        
        private Encoding GetEncoding(string encodingName)
        {
            return encodingName?.ToLowerInvariant() switch
            {
                "utf8" => Encoding.UTF8,
                "utf16" => Encoding.Unicode,
                "utf32" => Encoding.UTF32,
                "ascii" => Encoding.ASCII,
                "unicode" => Encoding.Unicode,
                _ => Encoding.UTF8
            };
        }
        
        private string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double size = bytes;
            int order = 0;
            
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }
            
            return $"{size:0.##} {sizes[order]}";
        }
        
        private class FileContent
        {
            public List<string> Lines { get; set; } = new List<string>();
            public int TotalLines { get; set; }
            public int ReadLines { get; set; }
        }
    }
}
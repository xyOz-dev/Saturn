using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using Saturn.Core.Workspace;
using Saturn.Tools.Core;

namespace Saturn.Tools
{
    public class WriteFileTool : ToolBase
    {
        private const long MaxFileSize = 10 * 1024 * 1024;
        
        public override string Name => "write_file";
        
        public override string Description => @"Create new files or overwrite existing files with specified content.

When to use:
- Creating new source code files
- Writing configuration files
- Creating documentation
- Saving generated content
- Creating test files

How to use:
- Set 'path' to the file location (will create directories if needed)
- Provide 'content' as the file contents
- Use 'overwrite' to control existing file behavior
- Set 'encoding' if needed (default: UTF-8)

Safety features:
- Prevents writing outside working directory
- Validates file size limits
- Supports atomic writes";
        
        protected override Dictionary<string, object> GetParameterProperties()
        {
            return new Dictionary<string, object>
            {
                { "path", new Dictionary<string, object>
                    {
                        { "type", "string" },
                        { "description", "Path where the file should be created" }
                    }
                },
                { "content", new Dictionary<string, object>
                    {
                        { "type", "string" },
                        { "description", "Content to write to the file" }
                    }
                },
                { "overwrite", new Dictionary<string, object>
                    {
                        { "type", "boolean" },
                        { "default", false },
                        { "description", "Overwrite if file exists (default: false). Set to true when intentionally replacing an existing file" }
                    }
                },
                { "createDirectories", new Dictionary<string, object>
                    {
                        { "type", "boolean" },
                        { "default", true },
                        { "description", "Create parent directories if they don't exist (default: true)" }
                    }
                },
                { "encoding", new Dictionary<string, object>
                    {
                        { "type", "string" },
                        { "enum", new[] { "UTF8", "ASCII", "Unicode", "UTF32" } },
                        { "default", "UTF8" },
                        { "description", "File encoding (default: UTF8)" }
                    }
                }
            };
        }
        
        protected override string[] GetRequiredParameters()
        {
            return new[] { "path", "content" };
        }
        
        public override string GetDisplaySummary(Dictionary<string, object> parameters)
        {
            var path = GetParameter<string>(parameters, "path", "");
            var content = GetParameter<string>(parameters, "content", "");
            var filename = string.IsNullOrEmpty(path) ? "unknown" : System.IO.Path.GetFileName(path);
            
            var contentBytes = Encoding.UTF8.GetByteCount(content);
            return $"Writing to {filename} ({FormatByteSize(contentBytes)})";
        }
        
        public override async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters)
        {
            var path = GetParameter<string>(parameters, "path");
            var content = GetParameter<string>(parameters, "content");
            var overwrite = GetParameter<bool>(parameters, "overwrite", false);
            var createDirectories = GetParameter<bool>(parameters, "createDirectories", true);
            var encodingName = GetParameter<string>(parameters, "encoding", "UTF8");
            
            if (string.IsNullOrEmpty(path))
            {
                return CreateErrorResult("File path cannot be empty");
            }
            
            if (content == null)
            {
                content = "";
            }
            
            try
            {
                ValidatePathSecurity(path);
                var fullPath = Path.GetFullPath(path, WorkspaceManager.CurrentWorkspace);
                
                var encoding = GetEncoding(encodingName);
                var bytes = encoding.GetBytes(content);
                
                if (bytes.Length > MaxFileSize)
                {
                    return CreateErrorResult($"Content too large ({bytes.Length} bytes). Maximum size is {MaxFileSize} bytes");
                }
                
                var fileExisted = File.Exists(fullPath);
                if (fileExisted && !overwrite)
                {
                    return CreateErrorResult($"File already exists: {fullPath}. Set overwrite=true to replace it");
                }
                
                var directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    if (!Directory.Exists(directory))
                    {
                        if (createDirectories)
                        {
                            Directory.CreateDirectory(directory);
                        }
                        else
                        {
                            return CreateErrorResult($"Directory does not exist: {directory}. Set createDirectories=true to create it");
                        }
                    }
                }
                
                var tempPath = $"{fullPath}.tmp_{Guid.NewGuid():N}";
                try
                {
                    await File.WriteAllBytesAsync(tempPath, bytes);

                    // Atomic replace: the original file is never deleted before the new
                    // content is in place, so a crash cannot lose both.
                    File.Move(tempPath, fullPath, overwrite: true);
                }
                finally
                {
                    if (File.Exists(tempPath))
                    {
                        try { File.Delete(tempPath); } catch { }
                    }
                }

                var fileInfo = new FileInfo(fullPath);
                var result = new
                {
                    Path = fullPath,
                    Size = fileInfo.Length,
                    Created = !fileExisted,
                    Encoding = encodingName
                };

                var action = fileExisted ? "Overwrote" : "Created";
                var message = $"{action} file: {fullPath} ({FormatFileSize(fileInfo.Length)})";
                
                return CreateSuccessResult(result, message);
            }
            catch (Exception ex)
            {
                return CreateErrorResult($"Failed to write file: {ex.Message}");
            }
        }
        
        private void ValidatePathSecurity(string path)
        {
            PathSecurity.ValidateInsideWorkingDirectory(path);

            var fullPath = Path.GetFullPath(path, WorkspaceManager.CurrentWorkspace);
            var invalidChars = Path.GetInvalidFileNameChars();
            var fileName = Path.GetFileName(fullPath);
            if (fileName.IndexOfAny(invalidChars) >= 0)
            {
                throw new ArgumentException($"File name contains invalid characters: {fileName}");
            }
        }
        
        private Encoding GetEncoding(string encodingName)
        {
            return encodingName?.ToUpperInvariant() switch
            {
                "UTF8" or "UTF-8" => Encoding.UTF8,
                "ASCII" => Encoding.ASCII,
                "UNICODE" or "UTF16" or "UTF-16" => Encoding.Unicode,
                "UTF32" or "UTF-32" => Encoding.UTF32,
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
    }
}
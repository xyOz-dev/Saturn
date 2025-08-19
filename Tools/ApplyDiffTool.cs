using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using Saturn.Tools.Core;

namespace Saturn.Tools
{
    public class ApplyDiffTool : ToolBase
    {
        public override string Name => "apply_diff";

        public override string Description => @"Apply targeted file changes (add, update, delete) encoded as
patch blocks. Use this tool for precise, small edits rather than large rewrites.

Supported operations:
- Add file:    '*** Add File: path/to/file' followed by lines prefixed with '+'.
- Update file: '*** Update File: path/to/file' followed by hunks starting with
  '@@ context @@' and lines prefixed with ' ' (keep), '-' (remove), '+' (add).
- Delete file: '*** Delete File: path/to/file' (no body required).

Rules and guidance:
- **ALWAYS** read the the target file before attempting any actions.
- Context lines inside '@@ ... @@' must be unique in the target file and match the
  file content exactly (trimmed) to locate the hunk position reliably.
- Keep hunks small and well-scoped. Prefer multiple targeted hunks over large
  ambiguous changes.
- Use dryRun=true to validate the patch and preview statistics without writing
  files.
- This tool enforces path security and will reject attempts at path traversal or
  edits outside the working directory.";

        protected override Dictionary<string, object> GetParameterProperties()
        {
            return new Dictionary<string, object>
            {
                ["patchText"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["description"] = "Patch text describing one or more file operations using " +
                             "the tool's patch format. Required. Format examples:\n" +
                             "  Add:    '*** Add File: path' then lines starting with '+'\n" +
                             "  Update: '*** Update File: path' then '@@ context @@' hunks " +
                             "and lines starting with ' ', '-' or '+'\n" +
                             "  Delete: '*** Delete File: path' (no body)"
                },
                ["dryRun"] = new Dictionary<string, object>
                {
                   ["type"] = "boolean",
                   ["description"] = "If true, validate the patch and return what would be " +
                             "changed without modifying files. Defaults to false."
                }
            };
        }
        
        protected override string[] GetRequiredParameters()
        {
            return new[] { "patchText" };
        }
        
        public override string GetDisplaySummary(Dictionary<string, object> parameters)
        {
            var patchText = GetParameter<string>(parameters, "patchText", "");
            
            var lines = patchText.Split('\n');
            var files = new HashSet<string>();
            int additions = 0;
            int deletions = 0;
            
            foreach (var line in lines)
            {
                if (line.StartsWith("*** Add File:") || line.StartsWith("*** Update File:") || line.StartsWith("*** Delete File:"))
                {
                    var parts = line.Split(':');
                    if (parts.Length > 1)
                    {
                        var filename = parts[1].Trim();
                        files.Add(System.IO.Path.GetFileName(filename));
                    }
                }
                else if (line.StartsWith("+") && !line.StartsWith("+++"))
                {
                    additions++;
                }
                else if (line.StartsWith("-") && !line.StartsWith("---"))
                {
                    deletions++;
                }
            }
            
            var filesList = files.Count > 0 ? string.Join(", ", files.Take(3)) : "files";
            if (files.Count > 3)
            {
                filesList += $" +{files.Count - 3}";
            }
            
            return $"Patching {filesList} ({additions}+, {deletions}-)";
        }
        
        private const long MaxFileSize = 100 * 1024 * 1024;
        private static readonly HashSet<string> FileLocks = new HashSet<string>();
        
        public override async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters)
        {
            var patchText = GetParameter<string>(parameters, "patchText");
            var dryRun = GetParameter<bool>(parameters, "dryRun", false);
            
            if (string.IsNullOrEmpty(patchText))
            {
                return CreateErrorResult("Patch text cannot be empty");
            }
            
            try
            {
                var filesNeeded = IdentifyFilesNeeded(patchText);
                var filesToAdd = IdentifyFilesAdded(patchText);
                
                await ValidateFilesForReading(filesNeeded);
                await ValidateFilesForAdding(filesToAdd);
                
                var currentFiles = await LoadCurrentFiles(filesNeeded);

                var operations = ParsePatchText(patchText, currentFiles);
                
                var allFiles = filesNeeded.Union(filesToAdd).ToList();
                var lockedFiles = new List<string>();
                
                try
                {
                    foreach (var file in allFiles)
                    {
                        var absPath = Path.GetFullPath(file);
                        lock (FileLocks)
                        {
                            if (FileLocks.Contains(absPath))
                            {
                                return CreateErrorResult($"File is currently being modified by another operation: {file}");
                            }
                            FileLocks.Add(absPath);
                            lockedFiles.Add(absPath);
                        }
                    }
                    
                    var commit = ConvertToCommit(operations, currentFiles);
                
                var stats = CalculateStatistics(commit);
                
                if (dryRun)
                {
                    var dryRunResult = new
                    {
                        DryRun = true,
                        ChangedFiles = stats.ChangedFiles,
                        TotalAdditions = stats.Additions,
                        TotalRemovals = stats.Removals,
                        FileCount = stats.ChangedFiles.Count,
                        Operations = operations.Select(op => new { op.Type, op.FilePath }).ToList()
                    };
                    
                    var dryRunOutput = $"[DRY RUN] Patch validation successful. Would change {stats.ChangedFiles.Count} files, {stats.Additions} additions, {stats.Removals} removals";
                    
                    return CreateSuccessResult(dryRunResult, dryRunOutput);
                }
                
                await ApplyCommit(commit);
                
                var result = new
                {
                    ChangedFiles = stats.ChangedFiles,
                    TotalAdditions = stats.Additions,
                    TotalRemovals = stats.Removals,
                    FileCount = stats.ChangedFiles.Count
                };
                
                var formattedOutput = $"Patch applied successfully. {stats.ChangedFiles.Count} files changed, {stats.Additions} additions, {stats.Removals} removals";
                
                    return CreateSuccessResult(result, formattedOutput);
                }
                finally
                {
                    lock (FileLocks)
                    {
                        foreach (var file in lockedFiles)
                        {
                            FileLocks.Remove(file);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return CreateErrorResult($"Failed to apply patch: {ex.Message}");
            }
        }
        
        private List<string> IdentifyFilesNeeded(string patchText)
        {
            var files = new List<string>();
            var lines = patchText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            
            foreach (var line in lines)
            {
                if (line.StartsWith("*** Update File:") || line.StartsWith("*** Delete File:"))
                {
                    var parts = line.Split(':', 2);
                    if (parts.Length > 1)
                    {
                        var filePath = parts[1].Trim();
                        if (!string.IsNullOrEmpty(filePath))
                        {
                            files.Add(filePath);
                        }
                    }
                }
            }
            
            return files;
        }
        
        private List<string> IdentifyFilesAdded(string patchText)
        {
            var files = new List<string>();
            var lines = patchText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            
            foreach (var line in lines)
            {
                if (line.StartsWith("*** Add File:"))
                {
                    var parts = line.Split(':', 2);
                    if (parts.Length > 1)
                    {
                        var filePath = parts[1].Trim();
                        if (!string.IsNullOrEmpty(filePath))
                        {
                            files.Add(filePath);
                        }
                    }
                }
            }
            
            return files;
        }
        
        private async Task ValidateFilesForReading(List<string> files)
        {
            foreach (var filePath in files)
            {
                ValidatePathSecurity(filePath);
                var absPath = Path.GetFullPath(filePath);
                
                if (!File.Exists(absPath))
                {
                    throw new FileNotFoundException($"File not found: {absPath}");
                }
                
                var fileInfo = new FileInfo(absPath);
                if (fileInfo.Attributes.HasFlag(FileAttributes.Directory))
                {
                    throw new InvalidOperationException($"Path is a directory, not a file: {absPath}");
                }
                
                if (fileInfo.Length > MaxFileSize)
                {
                    throw new InvalidOperationException($"File too large ({fileInfo.Length} bytes). Maximum size is {MaxFileSize} bytes: {absPath}");
                }
            }
            
            await Task.CompletedTask;
        }
        
        private async Task ValidateFilesForAdding(List<string> files)
        {
            foreach (var filePath in files)
            {
                ValidatePathSecurity(filePath);
                var absPath = Path.GetFullPath(filePath);
                
                if (File.Exists(absPath))
                {
                    throw new InvalidOperationException($"File already exists and cannot be added: {absPath}");
                }
            }
            
            await Task.CompletedTask;
        }
        
        private async Task<Dictionary<string, string>> LoadCurrentFiles(List<string> files)
        {
            var currentFiles = new Dictionary<string, string>();
            
            foreach (var filePath in files)
            {
                ValidatePathSecurity(filePath);
                var absPath = Path.GetFullPath(filePath);
                var content = await File.ReadAllTextAsync(absPath);
                currentFiles[filePath] = content;
            }
            
            return currentFiles;
        }
        
        private List<PatchOperation> ParsePatchText(string patchText, Dictionary<string, string> currentFiles)
        {
            var operations = new List<PatchOperation>();
            var lines = patchText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            var i = 0;
            
            while (i < lines.Length)
            {
                var line = lines[i];
                
                if (line.StartsWith("*** Update File:"))
                {
                    var parts = line.Split(':', 2);
                    if (parts.Length < 2)
                    {
                        throw new InvalidOperationException($"Invalid update file declaration at line {i + 1}: missing file path");
                    }
                    var filePath = parts[1]?.Trim();
                    if (string.IsNullOrEmpty(filePath))
                    {
                        throw new InvalidOperationException($"Invalid update file declaration at line {i + 1}: empty file path");
                    }
                    
                    var hunks = new List<PatchHunk>();
                    i++;
                    
                    while (i < lines.Length && !lines[i].StartsWith("***"))
                    {
                        if (lines[i].StartsWith("@@") && lines[i].EndsWith("@@") && lines[i].Length > 4)
                        {
                            var contextLine = lines[i].Substring(2, lines[i].Length - 4).Trim();
                            if (string.IsNullOrEmpty(contextLine))
                            {
                                throw new InvalidOperationException($"Empty context line at line {i + 1}");
                            }
                            var changes = new List<PatchChange>();
                            i++;
                            
                            while (i < lines.Length && !lines[i].StartsWith("@@") && !lines[i].StartsWith("***"))
                            {
                                var changeLine = lines[i];
                                if (changeLine.StartsWith(" "))
                                {
                                    changes.Add(new PatchChange { Type = ChangeType.Keep, Content = changeLine.Substring(1) });
                                }
                                else if (changeLine.StartsWith("-"))
                                {
                                    changes.Add(new PatchChange { Type = ChangeType.Remove, Content = changeLine.Substring(1) });
                                }
                                else if (changeLine.StartsWith("+"))
                                {
                                    changes.Add(new PatchChange { Type = ChangeType.Add, Content = changeLine.Substring(1) });
                                }
                                i++;
                            }
                            
                            hunks.Add(new PatchHunk { ContextLine = contextLine, Changes = changes });
                        }
                        else
                        {
                            i++;
                        }
                    }
                    
                    operations.Add(new PatchOperation { Type = OperationType.Update, FilePath = filePath, Hunks = hunks });
                }
                else if (line.StartsWith("*** Add File:"))
                {
                    var parts = line.Split(':', 2);
                    if (parts.Length < 2)
                    {
                        throw new InvalidOperationException($"Invalid add file declaration at line {i + 1}: missing file path");
                    }
                    var filePath = parts[1]?.Trim();
                    if (string.IsNullOrEmpty(filePath))
                    {
                        throw new InvalidOperationException($"Invalid add file declaration at line {i + 1}: empty file path");
                    }
                    
                    var content = new StringBuilder();
                    i++;
                    
                    while (i < lines.Length && !lines[i].StartsWith("***"))
                    {
                        if (lines[i].StartsWith("+"))
                        {
                            content.AppendLine(lines[i].Substring(1));
                        }
                        i++;
                    }
                    
                    var finalContent = content.ToString();
                    if (finalContent.EndsWith(Environment.NewLine))
                    {
                        finalContent = finalContent.Substring(0, finalContent.Length - Environment.NewLine.Length);
                    }
                    
                    operations.Add(new PatchOperation { Type = OperationType.Add, FilePath = filePath, Content = finalContent });
                }
                else if (line.StartsWith("*** Delete File:"))
                {
                    var parts = line.Split(':', 2);
                    if (parts.Length >= 2)
                    {
                        var filePath = parts[1]?.Trim();
                        if (!string.IsNullOrEmpty(filePath))
                        {
                            operations.Add(new PatchOperation { Type = OperationType.Delete, FilePath = filePath });
                        }
                    }
                    i++;
                }
                else
                {
                    i++;
                }
            }
            
            return operations;
        }
        
        private Commit ConvertToCommit(List<PatchOperation> operations, Dictionary<string, string> currentFiles)
        {
            var changes = new Dictionary<string, FileChange>();
            
            foreach (var op in operations)
            {
                if (op.Type == OperationType.Delete)
                {
                    changes[op.FilePath] = new FileChange
                    {
                        Type = ChangeType.Delete,
                        OldContent = currentFiles.ContainsKey(op.FilePath) ? currentFiles[op.FilePath] : ""
                    };
                }
                else if (op.Type == OperationType.Add)
                {
                    changes[op.FilePath] = new FileChange
                    {
                        Type = ChangeType.Add,
                        NewContent = op.Content ?? ""
                    };
                }
                else if (op.Type == OperationType.Update && op.Hunks != null)
                {
                    var originalContent = currentFiles.ContainsKey(op.FilePath) ? currentFiles[op.FilePath] : "";
                    var lineEnding = DetectLineEnding(originalContent);
                    var lines = originalContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();
                    
                    foreach (var hunk in op.Hunks)
                    {
                        if (hunk.ContextLine == null)
                        {
                            throw new InvalidOperationException("Context line cannot be null");
                        }
                        
                        var contextIndex = lines.FindIndex(line => line.Trim() == hunk.ContextLine.Trim());
                        if (contextIndex == -1)
                        {
                            throw new InvalidOperationException($"Context line not found: {hunk.ContextLine}");
                        }
                        
                        var currentIndex = contextIndex;
                        foreach (var change in hunk.Changes)
                        {
                            if (change.Type == ChangeType.Keep)
                            {
                                currentIndex++;
                            }
                            else if (change.Type == ChangeType.Remove)
                            {
                                if (currentIndex < lines.Count)
                                {
                                    lines.RemoveAt(currentIndex);
                                }
                            }
                            else if (change.Type == ChangeType.Add)
                            {
                                lines.Insert(currentIndex, change.Content ?? "");
                                currentIndex++;
                            }
                        }
                    }
                    
                    changes[op.FilePath] = new FileChange
                    {
                        Type = ChangeType.Update,
                        OldContent = originalContent,
                        NewContent = string.Join(lineEnding, lines)
                    };
                }
            }
            
            return new Commit { Changes = changes };
        }
        
        private async Task ApplyCommit(Commit commit)
        {
            foreach (var kvp in commit.Changes)
            {
                var filePath = kvp.Key;
                var change = kvp.Value;
                ValidatePathSecurity(filePath);
                var absPath = Path.GetFullPath(filePath);
                
                if (change.Type == ChangeType.Delete)
                {
                    File.Delete(absPath);
                }
                else if (change.NewContent != null)
                {
                    var directory = Path.GetDirectoryName(absPath);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }
                    
                    await File.WriteAllTextAsync(absPath, change.NewContent);
                }
            }
        }
        
        private PatchStatistics CalculateStatistics(Commit commit)
        {
            var stats = new PatchStatistics
            {
                ChangedFiles = new List<string>(),
                Additions = 0,
                Removals = 0
            };
            
            foreach (var kvp in commit.Changes)
            {
                var filePath = Path.GetFullPath(kvp.Key);
                stats.ChangedFiles.Add(filePath);
                
                if (kvp.Value.Type == ChangeType.Add)
                {
                    var newLines = (kvp.Value.NewContent ?? "").Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).Length;
                    stats.Additions += newLines;
                }
                else if (kvp.Value.Type == ChangeType.Delete)
                {
                    var oldLines = (kvp.Value.OldContent ?? "").Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).Length;
                    stats.Removals += oldLines;
                }
                else if (kvp.Value.Type == ChangeType.Update)
                {
                    var oldLines = (kvp.Value.OldContent ?? "").Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                    var newLines = (kvp.Value.NewContent ?? "").Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                    
                    var maxLines = Math.Max(oldLines.Length, newLines.Length);
                    for (int i = 0; i < maxLines; i++)
                    {
                        var oldLine = i < oldLines.Length ? oldLines[i] : null;
                        var newLine = i < newLines.Length ? newLines[i] : null;
                        
                        if (oldLine == null && newLine != null)
                        {
                            stats.Additions++;
                        }
                        else if (oldLine != null && newLine == null)
                        {
                            stats.Removals++;
                        }
                        else if (oldLine != newLine)
                        {
                            stats.Additions++;
                            stats.Removals++;
                        }
                    }
                }
            }
            
            return stats;
        }
        
        private enum OperationType
        {
            Add,
            Update,
            Delete
        }
        
        private enum ChangeType
        {
            Add,
            Update,
            Delete,
            Keep,
            Remove
        }
        
        private class PatchOperation
        {
            public OperationType Type { get; set; }
            public string FilePath { get; set; } = string.Empty;
            public List<PatchHunk> Hunks { get; set; } = new List<PatchHunk>();
            public string Content { get; set; } = string.Empty;
        }
        
        private class PatchHunk
        {
            public string ContextLine { get; set; } = string.Empty;
            public List<PatchChange> Changes { get; set; } = new List<PatchChange>();
        }
        
        private class PatchChange
        {
            public ChangeType Type { get; set; }
            public string Content { get; set; } = string.Empty;
        }
        
        private class FileChange
        {
            public ChangeType Type { get; set; }
            public string OldContent { get; set; } = string.Empty;
            public string NewContent { get; set; } = string.Empty;
        }
        
        private class Commit
        {
            public Dictionary<string, FileChange> Changes { get; set; } = new Dictionary<string, FileChange>();
        }
        
        private class PatchStatistics
        {
            public List<string> ChangedFiles { get; set; } = new List<string>();
            public int Additions { get; set; }
            public int Removals { get; set; }
        }
        
        private void ValidatePathSecurity(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("File path cannot be null or empty");
            }
            
            if (filePath.Contains("..") || filePath.Contains("~"))
            {
                throw new SecurityException($"Invalid file path: {filePath}. Path traversal attempts are not allowed.");
            }
            
            try
            {
                var fullPath = Path.GetFullPath(filePath);
                var currentDirectory = Path.GetFullPath(Directory.GetCurrentDirectory());
                
                if (!fullPath.StartsWith(currentDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    throw new SecurityException($"Access denied: Path '{filePath}' is outside the working directory.");
                }
            }
            catch (Exception ex) when (!(ex is SecurityException))
            {
                throw new ArgumentException($"Invalid file path: {filePath}", ex);
            }
        }
        
        private string DetectLineEnding(string content)
        {
            if (string.IsNullOrEmpty(content))
            {
                return Environment.NewLine;
            }
            
            var crlfCount = 0;
            var lfCount = 0;
            
            for (int i = 0; i < content.Length - 1; i++)
            {
                if (content[i] == '\r' && content[i + 1] == '\n')
                {
                    crlfCount++;
                    i++;
                }
                else if (content[i] == '\n')
                {
                    lfCount++;
                }
            }
            
            if (content[content.Length - 1] == '\n' && (content.Length == 1 || content[content.Length - 2] != '\r'))
            {
                lfCount++;
            }
            
            return crlfCount >= lfCount ? "\r\n" : "\n";
        }
    }
}
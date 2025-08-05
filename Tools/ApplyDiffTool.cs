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
        
        public override string Description => @"Apply unified diff-style patches to files. Supports adding, updating, and deleting files with context-aware patching.

Patch format:
- Add file: *** Add File: path/to/file.txt
  +line 1 content
  +line 2 content
  
- Update file: *** Update File: path/to/file.txt
  @@ context line to find location @@
   keep this line unchanged
  -remove this line
  +add this line
  
- Delete file: *** Delete File: path/to/file.txt

Important:
- Files to be updated MUST be read first using read_file tool
- Context lines must be unique enough to avoid ambiguity
- Use space prefix for unchanged lines, - for removals, + for additions
- Multiple operations can be combined in one patch";
        
        protected override Dictionary<string, object> GetParameterProperties()
        {
            return new Dictionary<string, object>
            {
                { "patchText", new Dictionary<string, object>
                    {
                        { "type", "string" },
                        { "description", "The full patch text that describes all changes to be made. Use the format described in the tool description." }
                    }
                },
                { "dryRun", new Dictionary<string, object>
                    {
                        { "type", "boolean" },
                        { "description", "If true, validates the patch and shows what would be changed without actually modifying files. Default is false." }
                    }
                }
            };
        }
        
        protected override string[] GetRequiredParameters()
        {
            return new[] { "patchText" };
        }
        
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
                
                var (operations, fuzz) = ParsePatchText(patchText, currentFiles);
                
                if (fuzz > 3)
                {
                    return CreateErrorResult($"Patch contains fuzzy matches (fuzz level: {fuzz}). Please make your context lines more precise");
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
        
        private (List<PatchOperation> operations, int fuzz) ParsePatchText(string patchText, Dictionary<string, string> currentFiles)
        {
            var operations = new List<PatchOperation>();
            var lines = patchText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            var fuzz = 0;
            var i = 0;
            
            while (i < lines.Length)
            {
                var line = lines[i];
                
                if (line.StartsWith("*** Update File:"))
                {
                    var parts = line.Split(':', 2);
                    if (parts.Length < 2)
                    {
                        i++;
                        continue;
                    }
                    var filePath = parts[1]?.Trim();
                    if (string.IsNullOrEmpty(filePath))
                    {
                        i++;
                        continue;
                    }
                    
                    var hunks = new List<PatchHunk>();
                    i++;
                    
                    while (i < lines.Length && !lines[i].StartsWith("***"))
                    {
                        if (lines[i].StartsWith("@@"))
                        {
                            var contextLine = lines[i].Substring(2).Trim();
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
                        i++;
                        continue;
                    }
                    var filePath = parts[1]?.Trim();
                    if (string.IsNullOrEmpty(filePath))
                    {
                        i++;
                        continue;
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
            
            return (operations, fuzz);
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
                    var lines = originalContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();
                    
                    foreach (var hunk in op.Hunks)
                    {
                        var contextIndex = lines.FindIndex(line => line.Contains(hunk.ContextLine));
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
                                lines.Insert(currentIndex, change.Content);
                                currentIndex++;
                            }
                        }
                    }
                    
                    changes[op.FilePath] = new FileChange
                    {
                        Type = ChangeType.Update,
                        OldContent = originalContent,
                        NewContent = string.Join(Environment.NewLine, lines)
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
                
                var oldLines = (kvp.Value.OldContent ?? "").Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).Length;
                var newLines = (kvp.Value.NewContent ?? "").Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).Length;
                
                if (kvp.Value.Type == ChangeType.Add)
                {
                    stats.Additions += newLines;
                }
                else if (kvp.Value.Type == ChangeType.Delete)
                {
                    stats.Removals += oldLines;
                }
                else if (kvp.Value.Type == ChangeType.Update)
                {
                    var diff = newLines - oldLines;
                    if (diff > 0)
                    {
                        stats.Additions += diff;
                    }
                    else
                    {
                        stats.Removals += Math.Abs(diff);
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
            public string FilePath { get; set; }
            public List<PatchHunk> Hunks { get; set; }
            public string Content { get; set; }
        }
        
        private class PatchHunk
        {
            public string ContextLine { get; set; }
            public List<PatchChange> Changes { get; set; }
        }
        
        private class PatchChange
        {
            public ChangeType Type { get; set; }
            public string Content { get; set; }
        }
        
        private class FileChange
        {
            public ChangeType Type { get; set; }
            public string OldContent { get; set; }
            public string NewContent { get; set; }
        }
        
        private class Commit
        {
            public Dictionary<string, FileChange> Changes { get; set; }
        }
        
        private class PatchStatistics
        {
            public List<string> ChangedFiles { get; set; }
            public int Additions { get; set; }
            public int Removals { get; set; }
        }
        
        private void ValidatePathSecurity(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("File path cannot be null or empty");
            }
            
            if (filePath.Contains("..") || filePath.Contains("~") || Path.IsPathRooted(filePath) && filePath.StartsWith("/"))
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
    }
}
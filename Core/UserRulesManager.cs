using System;
using System.IO;
using System.Threading.Tasks;

namespace Saturn.Core
{
    public static class UserRulesManager
    {
        private static readonly string RulesFileName = "rules.md";
        private static readonly int MaxFileSize = 1024 * 1024; // 1MB
        private static readonly int MaxContentLength = 50000; // 50k characters
        
        public static string GetRulesFilePath()
        {
            var saturnDir = Path.Combine(Environment.CurrentDirectory, ".saturn");
            if (!Directory.Exists(saturnDir))
            {
                Directory.CreateDirectory(saturnDir);
            }
            return Path.Combine(saturnDir, RulesFileName);
        }
        
        public static bool RulesFileExists()
        {
            return File.Exists(GetRulesFilePath());
        }
        
        public static async Task<string> LoadRulesAsync()
        {
            var rulesPath = GetRulesFilePath();
            
            if (!File.Exists(rulesPath))
                return string.Empty;
                
            try
            {
                var fileInfo = new FileInfo(rulesPath);
                if (fileInfo.Length > MaxFileSize)
                {
                    throw new InvalidOperationException($"Rules file too large (max {MaxFileSize / (1024 * 1024)}MB)");
                }
                
                var content = await File.ReadAllTextAsync(rulesPath).ConfigureAwait(false);
                
                if (content.Length > MaxContentLength)
                {
                    content = content.Substring(0, MaxContentLength) + "\n[Content truncated - rules file too long]";
                }
                
                return content;
            }
            catch (UnauthorizedAccessException)
            {
                throw new InvalidOperationException("Access denied to rules file");
            }
        }
        
        public static async Task<bool> SaveRulesAsync(string content, bool createBackup = true)
        {
            var rulesPath = GetRulesFilePath();
            
            try
            {
                if (content != null && content.Length > MaxContentLength)
                {
                    throw new ArgumentException($"Content too long (max {MaxContentLength} characters)");
                }
                
                if (createBackup && File.Exists(rulesPath))
                {
                    var backupPath = rulesPath + ".backup";
                    File.Copy(rulesPath, backupPath, overwrite: true);
                }
                
                if (string.IsNullOrWhiteSpace(content))
                {
                    if (File.Exists(rulesPath))
                    {
                        File.Delete(rulesPath);
                    }
                    return true;
                }
                
                // Save content
                await File.WriteAllTextAsync(rulesPath, content).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Error saving rules: {ex.Message}", ex);
            }
        }
        
        public static bool DeleteRulesFile()
        {
            try
            {
                var rulesPath = GetRulesFilePath();
                if (File.Exists(rulesPath))
                {
                    File.Delete(rulesPath);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }
        
        public static async Task<bool> CreateDefaultRulesAsync()
        {
            var defaultContent = GetDefaultRulesTemplate();
            
            try
            {
                await SaveRulesAsync(defaultContent, createBackup: false).ConfigureAwait(false);
                return true;
            }
            catch
            {
                return false;
            }
        }
        
        public static string GetDefaultRulesTemplate()
        {
            return @"# User Rules

These rules will be applied to all AI interactions in this workspace.

## General Guidelines
- Follow established code conventions and patterns
- Provide clear and concise explanations
- Focus on maintainable and readable solutions

## Project-Specific Rules
- Add your custom rules here
- Use markdown formatting for clarity
- Examples:
  - Always use async/await for database operations
  - Include XML documentation for public methods
  - Follow specific naming conventions

## Response Format
- Prefer structured responses when appropriate
- Include reasoning for architectural decisions
- Mention potential trade-offs or alternatives

---
*These rules are automatically included in the system prompt for every agent interaction.*";
        }
        
        public static FileInfo? GetRulesFileInfo()
        {
            var rulesPath = GetRulesFilePath();
            return File.Exists(rulesPath) ? new FileInfo(rulesPath) : null;
        }
        
        public static (bool exists, long size, DateTime lastModified) GetRulesFileStatus()
        {
            var rulesPath = GetRulesFilePath();
            
            if (!File.Exists(rulesPath))
                return (false, 0, DateTime.MinValue);
                
            var fileInfo = new FileInfo(rulesPath);
            return (true, fileInfo.Length, fileInfo.LastWriteTime);
        }
    }
}
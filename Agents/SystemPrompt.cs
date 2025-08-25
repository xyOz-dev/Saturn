using Saturn.Core;
using Saturn.Tools;
using Saturn.Tools.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Saturn.Agents
{
    public static class SystemPrompt
    {
        private const int MaxDirectoryResults = 200;
        private const string DirectorySectionStart = "\n<current_directory>";
        private const string DirectorySectionEnd = "</current_directory>\n";
        private const string UserRulesSectionStart = "\n<user_rules>";
        private const string UserRulesSectionEnd = "</user_rules>\n";

        public static async Task<string> Create(string prompt, bool includeDirectories = true, bool includeUserRules = true)
        {
            if (string.IsNullOrEmpty(prompt))
                throw new ArgumentException("Prompt cannot be null or empty", nameof(prompt));

            var output = new StringBuilder(prompt);

            if (includeDirectories)
            {
                var directoryView = await GenerateDirectoryView();
                output.AppendLine().Append(directoryView);
            }

            if (includeUserRules)
            {
                var userRules = await LoadUserRules();
                if (!string.IsNullOrEmpty(userRules))
                {
                    output.AppendLine().Append(userRules);
                }
            }

            var result = output.ToString();
            return result;
        }

        private static async Task<string> GenerateDirectoryView()
        {
            var listTool = new ListFilesTool();
            var parameters = new Dictionary<string, object>
            {
                { "recursive", true },
                { "maxResults", MaxDirectoryResults }
            };

            try
            {
                var result = await listTool.ExecuteAsync(parameters);

                return $"{DirectorySectionStart}\n{result.FormattedOutput}\n{DirectorySectionEnd}";
            }
            catch (Exception ex)
            {
                return $"{DirectorySectionStart}\nError retrieving directory information: {ex.Message}\n{DirectorySectionEnd}";
            }
        }

        private static async Task<string> LoadUserRules()
        {
            var (content, wasTruncated, error) = await UserRulesManager.LoadUserRules();
            
            if (!string.IsNullOrEmpty(error))
            {
                var escapedError = EscapeXmlContent(error);
                return $"{UserRulesSectionStart}\n{escapedError}\n{UserRulesSectionEnd}";
            }
            
            if (string.IsNullOrWhiteSpace(content))
                return string.Empty;
            
            var safeContent = EscapeXmlContent(content.Trim());
            return $"{UserRulesSectionStart}\n{safeContent}\n{UserRulesSectionEnd}";
        }

        private static string EscapeXmlContent(string content)
        {
            if (string.IsNullOrEmpty(content))
                return content;

            return content
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;");
        }
    }
}
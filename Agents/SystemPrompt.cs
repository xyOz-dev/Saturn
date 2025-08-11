using Saturn.Tools;
using Saturn.Tools.Core;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Saturn.Agents
{
    public static class SystemPrompt
    {
        private const int MaxDirectoryResults = 200;
        private const string DirectorySectionStart = "\n<current_directory>";
        private const string DirectorySectionEnd = "</current_directory>\n";

        public static async Task<string> Create(string prompt, bool includeDirectories = true)
        {
            if (string.IsNullOrEmpty(prompt))
                throw new ArgumentException("Prompt cannot be null or empty", nameof(prompt));

            var output = new StringBuilder(prompt);

            if (includeDirectories)
            {
                var directoryView = await GenerateDirectoryView();
                output.AppendLine().Append(directoryView);
            }

            var result = output.ToString();
            Console.WriteLine(result);
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
    }
}
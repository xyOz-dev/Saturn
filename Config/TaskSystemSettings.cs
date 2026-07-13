using System;
using System.IO;
using System.Text.Json;

namespace Saturn.Config
{
    public class TaskSystemSettings
    {
        public bool TrustMode { get; set; }
        public bool JudgeEnabled { get; set; } = true;
        public int ApprovalTimeoutMinutes { get; set; }
        public int SchedulerIntervalSeconds { get; set; } = 20;
        public int MaxWakesPerHour { get; set; } = 30;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private static string FilePath
        {
            get
            {
                var dir = Environment.GetEnvironmentVariable("SATURN_CONFIG_DIR")
                    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Saturn");
                return Path.Combine(dir, "tasksystem.json");
            }
        }

        public static TaskSystemSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var json = File.ReadAllText(FilePath);
                    return JsonSerializer.Deserialize<TaskSystemSettings>(json, JsonOptions) ?? new TaskSystemSettings();
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to load task system settings: {ex.Message}");
            }
            return new TaskSystemSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                var tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOptions));
                File.Move(tmp, FilePath, overwrite: true);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to save task system settings: {ex.Message}");
            }
        }
    }
}

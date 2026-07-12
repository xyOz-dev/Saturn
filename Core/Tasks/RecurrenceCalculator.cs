using System;
using NCrontab;
using Saturn.Data.Tasks;

namespace Saturn.Core.Tasks
{
    public static class RecurrenceCalculator
    {
        public static string? Validate(string kind, int? intervalSeconds, string? cron)
        {
            switch (kind)
            {
                case RecurrenceKinds.None:
                    return null;
                case RecurrenceKinds.Interval:
                    if (!intervalSeconds.HasValue || intervalSeconds.Value < 60)
                    {
                        return "Interval recurrence requires an interval of at least 60 seconds";
                    }
                    return null;
                case RecurrenceKinds.Cron:
                    if (string.IsNullOrWhiteSpace(cron))
                    {
                        return "Cron recurrence requires a cron expression";
                    }
                    return TryParseCron(cron, out _) ? null : $"Invalid cron expression: '{cron}'";
                default:
                    return $"Unknown recurrence kind '{kind}'";
            }
        }

        public static DateTime? GetNextOccurrenceUtc(string kind, int? intervalSeconds, string? cron, DateTime afterUtc)
        {
            switch (kind)
            {
                case RecurrenceKinds.Interval when intervalSeconds.HasValue:
                    return afterUtc.AddSeconds(intervalSeconds.Value);
                case RecurrenceKinds.Cron when !string.IsNullOrWhiteSpace(cron) && TryParseCron(cron, out var schedule):
                    // Cron expresses local wall-clock intent ("9am daily"); compute locally, store UTC.
                    var afterLocal = afterUtc.ToLocalTime();
                    var nextLocal = schedule!.GetNextOccurrence(afterLocal);
                    return DateTime.SpecifyKind(nextLocal, DateTimeKind.Local).ToUniversalTime();
                default:
                    return null;
            }
        }

        public static string Describe(string kind, int? intervalSeconds, string? cron)
        {
            switch (kind)
            {
                case RecurrenceKinds.Interval when intervalSeconds.HasValue:
                    var span = TimeSpan.FromSeconds(intervalSeconds.Value);
                    if (span.TotalDays >= 1 && span.TotalDays == Math.Floor(span.TotalDays))
                        return $"every {(span.TotalDays == 1 ? "day" : $"{(int)span.TotalDays} days")}";
                    if (span.TotalHours >= 1 && span.TotalHours == Math.Floor(span.TotalHours))
                        return $"every {(span.TotalHours == 1 ? "hour" : $"{(int)span.TotalHours} hours")}";
                    return $"every {(int)span.TotalMinutes} min";
                case RecurrenceKinds.Cron:
                    return $"cron: {cron}";
                default:
                    return "";
            }
        }

        public static bool TryParseCron(string expression, out CrontabSchedule? schedule)
        {
            schedule = CrontabSchedule.TryParse(expression);
            if (schedule == null)
            {
                schedule = CrontabSchedule.TryParse(expression, new CrontabSchedule.ParseOptions { IncludingSeconds = true });
            }
            return schedule != null;
        }
    }
}

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
                    if (!TryParseCron(cron, out _))
                    {
                        return $"Invalid cron expression: '{cron}'";
                    }
                    // Syntactically valid expressions can still describe a date that
                    // never exists (e.g. '0 0 30 2 *'); reject those up front.
                    if (GetNextOccurrenceUtc(RecurrenceKinds.Cron, null, cron, DateTime.UtcNow) == null)
                    {
                        return $"Cron expression '{cron}' never produces a future occurrence";
                    }
                    return null;
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
                    try
                    {
                        // Cron expresses local wall-clock intent ("9am daily"); compute locally, store UTC.
                        // Impossible schedules (e.g. Feb 30) make NCrontab return the end bound
                        // (or throw, depending on version); ten years comfortably covers the
                        // longest real gap, leap-day schedules included.
                        var afterLocal = afterUtc.ToLocalTime();
                        var limitLocal = afterLocal.AddYears(10);
                        var nextLocal = schedule!.GetNextOccurrence(afterLocal, limitLocal);
                        if (nextLocal >= limitLocal)
                        {
                            return null;
                        }
                        return DateTime.SpecifyKind(nextLocal, DateTimeKind.Local).ToUniversalTime();
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        return null;
                    }
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

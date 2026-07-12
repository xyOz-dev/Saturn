using System;
using System.Collections.Generic;

namespace Saturn.Data.Tasks
{
    public static class TaskScopes
    {
        public const string Global = "global";
        public const string Project = "project";
        public const string Agent = "agent";
        public static readonly string[] All = { Global, Project, Agent };
    }

    public static class TaskStatuses
    {
        public const string Pending = "pending";
        public const string InProgress = "in_progress";
        public const string Done = "done";
        public const string Failed = "failed";
        public const string Cancelled = "cancelled";
        public static readonly string[] All = { Pending, InProgress, Done, Failed, Cancelled };
        public static readonly string[] Open = { Pending, InProgress };
        public static bool IsTerminal(string status) => status is Done or Failed or Cancelled;
    }

    public static class RecurrenceKinds
    {
        public const string None = "none";
        public const string Interval = "interval";
        public const string Cron = "cron";
        public static readonly string[] All = { None, Interval, Cron };
    }

    public static class ClaimStatuses
    {
        public const string None = "none";
        public const string PendingApproval = "pending_approval";
        public const string Approved = "approved";
        public const string Denied = "denied";
    }

    public static class CatchUpPolicies
    {
        public const string RunOnce = "run_once";
        public const string Skip = "skip";
    }

    public static class WakeKinds
    {
        public const string TaskCompleted = "task_completed";
        public const string RecurrenceDue = "recurrence_due";
        public const string TaskUnblocked = "task_unblocked";
        public const string TaskReady = "task_ready";
        public const string OrphanRecovered = "orphan_recovered";
        public const string WaiterFallback = "waiter_fallback";
        public const string ClaimResolved = "claim_resolved";
    }

    public class SaturnTask
    {
        public string Id { get; set; } = NewId("tk");
        public string Scope { get; set; } = TaskScopes.Project;
        public string Board { get; set; } = "default";
        public string Title { get; set; } = "";
        public string? Notes { get; set; }
        public string Status { get; set; } = TaskStatuses.Pending;
        public string Priority { get; set; } = "normal";
        public int SortOrder { get; set; }
        public string CreatedBy { get; set; } = "user";
        public bool AgentAvailable { get; set; }
        public bool RequiresApproval { get; set; }
        public bool UserHandoffOnly { get; set; }
        public string ClaimStatus { get; set; } = ClaimStatuses.None;
        public string? ClaimedBy { get; set; }
        public string RecurrenceKind { get; set; } = RecurrenceKinds.None;
        public int? RecurrenceIntervalSeconds { get; set; }
        public string? RecurrenceCron { get; set; }
        public string CatchUpPolicy { get; set; } = CatchUpPolicies.RunOnce;
        public DateTime? NextRunAt { get; set; }
        public DateTime? LastRunAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }

        public bool IsRecurring => RecurrenceKind != RecurrenceKinds.None;

        public static string NewId(string prefix) => $"{prefix}_{Guid.NewGuid():N}"[..(prefix.Length + 13)];
    }

    public class TaskBlockerInfo
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Status { get; set; } = "";
        public bool Missing { get; set; }
    }

    public class TaskView
    {
        public SaturnTask Task { get; set; } = null!;
        public bool Blocked { get; set; }
        public List<TaskBlockerInfo> BlockedBy { get; set; } = new();
        public string? RecurrenceDescription { get; set; }
        public string? DispatchedTo { get; set; }
        public bool HasWaiters { get; set; }
    }

    public class TaskWaiter
    {
        public string Id { get; set; } = SaturnTask.NewId("wt");
        public string WaitTargetKind { get; set; } = "saturn_task";
        public string WaitTargetId { get; set; } = "";
        public string WaiterKind { get; set; } = "agent";
        public string? WaiterAgentId { get; set; }
        public string? WaiterAgentName { get; set; }
        public string? PromptTemplate { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? DeliveredAt { get; set; }
        public int DeliveryAttempts { get; set; }
    }

    public class TaskDispatch
    {
        public string Id { get; set; } = SaturnTask.NewId("dp");
        public string TaskId { get; set; } = "";
        public string? AgentManagerTaskId { get; set; }
        public string? AgentId { get; set; }
        public string? AgentName { get; set; }
        public DateTime StartedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }
        public bool? Success { get; set; }
        public string? Result { get; set; }
        public bool Orphaned { get; set; }
    }

    public class WakeItem
    {
        public string Id { get; set; } = SaturnTask.NewId("wk");
        public string Kind { get; set; } = "";
        public string? TaskId { get; set; }
        public string? DedupeKey { get; set; }
        public string Prompt { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? DeliveredAt { get; set; }
    }

    public class TaskRun
    {
        public string Id { get; set; } = SaturnTask.NewId("tr");
        public string TaskId { get; set; } = "";
        public DateTime ScheduledFor { get; set; }
        public DateTime FiredAt { get; set; } = DateTime.UtcNow;
        public string? Outcome { get; set; }
    }
}

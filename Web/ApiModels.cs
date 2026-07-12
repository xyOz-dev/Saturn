using System.Collections.Generic;

namespace Saturn.Web
{
    public record CreateAgentRequest(
        string Name,
        string Purpose,
        string? Model,
        double? Temperature,
        int? MaxTokens,
        string? SystemPrompt);

    public record HandOffRequest(string Task, Dictionary<string, object>? Context);

    public record TaskCreateRequest(
        string Title,
        string? Notes,
        string? Scope,
        string? Board,
        string? Priority,
        List<string>? BlockedBy,
        string? RecurrenceKind,
        int? RecurrenceIntervalSeconds,
        string? RecurrenceCron,
        string? CatchUpPolicy,
        bool? AgentAvailable,
        bool? RequiresApproval,
        bool? UserHandoffOnly);

    public record TaskUpdateRequest(
        string? Title,
        string? Notes,
        string? Status,
        string? Priority,
        string? Scope,
        string? Board,
        int? SortOrder,
        List<string>? BlockedBy,
        string? RecurrenceKind,
        int? RecurrenceIntervalSeconds,
        string? RecurrenceCron,
        string? CatchUpPolicy,
        bool? AgentAvailable,
        bool? RequiresApproval,
        bool? UserHandoffOnly);

    public record TaskCompleteRequest(bool? Success, string? Note);

    public record TaskDispatchRequest(string? AgentId);

    public record OrchestratorMessageRequest(string Message);

    public record ApprovalDecisionRequest(bool Approved);

    public record SettingsUpdateRequest(int? MaxConcurrentAgents, bool? RequireCommandApproval);
}

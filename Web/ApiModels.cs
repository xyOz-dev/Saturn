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

    public record ProviderSwitchRequest(string Provider, Dictionary<string, string?>? Settings, string? Model);

    public record ModelSwitchRequest(string Model);

    public record AgentConfigUpdateRequest(
        double? Temperature,
        int? MaxTokens,
        double? TopP,
        bool? EnableStreaming,
        bool? MaintainHistory,
        int? MaxHistoryMessages,
        bool? EnableUserRules,
        List<string>? ToolNames);

    public record UserRulesUpdateRequest(string Content);

    public record SubAgentDefaultsRequest(
        string? DefaultModel,
        double? DefaultTemperature,
        int? DefaultMaxTokens,
        double? DefaultTopP,
        bool? DefaultEnableTools,
        bool? EnableReviewStage,
        string? ReviewerModel,
        int? ReviewTimeoutSeconds,
        int? MaxRevisionCycles);

    public record SettingsUpdateRequest(
        int? MaxConcurrentAgents,
        bool? RequireCommandApproval,
        bool? TrustMode,
        bool? JudgeEnabled,
        int? ApprovalTimeoutMinutes,
        int? SchedulerIntervalSeconds,
        int? MaxWakesPerHour);
}

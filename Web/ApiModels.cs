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

    public record TodoCreateRequest(string Title, string? Notes, string? Priority);

    public record TodoUpdateRequest(string? Title, string? Notes, string? Status, string? Priority, int? Order);

    public record OrchestratorMessageRequest(string Message);

    public record ApprovalDecisionRequest(bool Approved);

    public record SettingsUpdateRequest(int? MaxConcurrentAgents, bool? RequireCommandApproval);
}

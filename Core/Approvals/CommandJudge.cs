using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Saturn.OpenRouter.Models.Api.Chat;
using Saturn.Providers;

namespace Saturn.Core.Approvals
{
    public enum JudgeDecision
    {
        Approve,
        Deny,
        Escalate
    }

    public record JudgeRequest(
        string Command,
        string WorkingDirectory,
        string AgentName,
        string? AgentPurpose,
        string? TaskDescription);

    public record JudgeVerdict(JudgeDecision Decision, string Reason);

    // LLM-based safety judge for sub-agent shell commands. Stateless: one
    // completion per judgment, no chat history, no tools. Any failure,
    // timeout or unparseable output escalates to the user (fail-closed).
    public class CommandJudge
    {
        private static readonly TimeSpan JudgeTimeout = TimeSpan.FromSeconds(60);

        private const string Rubric =
            @"You are a command-safety judge inside an autonomous multi-agent coding assistant.
A sub-agent wants to run a shell command in the user's repository. Decide whether it is safe.

APPROVE commands that are clearly routine for software work: reading state (git status, ls, dir, type),
building, testing, linting, formatting, package installs into the project, running project scripts.
DENY commands that are destructive or dangerous: deleting files/directories outside build artifacts,
rewriting git history, force pushes, modifying system settings, killing arbitrary processes,
downloading and executing remote code, accessing credentials or secrets, or anything targeting paths
outside the working directory.
ESCALATE anything ambiguous, unusual, or high-impact you are not confident about.

Respond with EXACTLY ONE line in this format and nothing else:
APPROVE: <short reason>
DENY: <short reason>
ESCALATE: <short reason>";

        private readonly ILlmClientSource _clientSource;
        private readonly Func<string> _modelProvider;

        public CommandJudge(ILlmClientSource clientSource, Func<string> modelProvider)
        {
            _clientSource = clientSource;
            _modelProvider = modelProvider;
        }

        public async Task<JudgeVerdict> JudgeAsync(JudgeRequest request, CancellationToken cancellationToken = default)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(JudgeTimeout);

                var context =
                    $"Agent: {request.AgentName}\n" +
                    $"Agent purpose: {request.AgentPurpose ?? "(unknown)"}\n" +
                    $"Current task: {request.TaskDescription ?? "(none recorded)"}\n" +
                    $"Working directory: {request.WorkingDirectory}\n\n" +
                    $"Command:\n{request.Command}";

                var chatRequest = new ChatCompletionRequest
                {
                    Model = _modelProvider(),
                    Temperature = 0,
                    MaxTokens = 200,
                    Messages = new[]
                    {
                        new Message { Role = "system", Content = JsonSerializer.SerializeToElement(Rubric) },
                        new Message { Role = "user", Content = JsonSerializer.SerializeToElement(context) }
                    }
                };

                var response = await _clientSource.Current.ChatAsync(chatRequest, cts.Token);
                var text = response?.Choices?.Length > 0 ? response.Choices[0].Message?.Content?.Trim() : null;
                if (string.IsNullOrWhiteSpace(text))
                {
                    return new JudgeVerdict(JudgeDecision.Escalate, "Judge returned no output");
                }

                return ParseVerdict(text);
            }
            catch (Exception ex)
            {
                return new JudgeVerdict(JudgeDecision.Escalate, $"Judge unavailable: {ex.Message}");
            }
        }

        private static JudgeVerdict ParseVerdict(string text)
        {
            foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (line.StartsWith("APPROVE:", StringComparison.OrdinalIgnoreCase))
                {
                    return new JudgeVerdict(JudgeDecision.Approve, line[8..].Trim());
                }
                if (line.StartsWith("DENY:", StringComparison.OrdinalIgnoreCase))
                {
                    return new JudgeVerdict(JudgeDecision.Deny, line[5..].Trim());
                }
                if (line.StartsWith("ESCALATE:", StringComparison.OrdinalIgnoreCase))
                {
                    return new JudgeVerdict(JudgeDecision.Escalate, line[9..].Trim());
                }
            }
            return new JudgeVerdict(JudgeDecision.Escalate, $"Unparseable judge output: {text[..Math.Min(120, text.Length)]}");
        }
    }
}

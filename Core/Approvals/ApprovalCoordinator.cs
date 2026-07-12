using System;
using System.Linq;
using System.Threading.Tasks;
using Saturn.Config;
using Saturn.Data.Tasks;
using Saturn.Tools.Core;
using Saturn.Web;

namespace Saturn.Core.Approvals
{
    // Tiered shell-command approval:
    //   1. Trust mode: everything auto-approves (audited).
    //   2. Sub-agent + judge enabled: an LLM judge approves/denies; escalations
    //      and judge failures fall through to the user queue (fail-closed).
    //   3. User queue: orchestrator commands, escalations, judge-off mode.
    public class ApprovalCoordinator : ICommandApprovalService
    {
        private readonly WebCommandApprovalService _userQueue;
        private readonly CommandJudge _judge;
        private readonly TaskSystemSettings _settings;
        private readonly EventHub _hub;
        private readonly TaskStore _store;

        public ApprovalCoordinator(
            WebCommandApprovalService userQueue,
            CommandJudge judge,
            TaskSystemSettings settings,
            EventHub hub,
            TaskStore store)
        {
            _userQueue = userQueue;
            _judge = judge;
            _settings = settings;
            _hub = hub;
            _store = store;
        }

        public async Task<bool> RequestApprovalAsync(string command, string workingDirectory)
        {
            var caller = AgentContext.Current;
            var agentName = caller?.AgentName;

            if (_settings.TrustMode)
            {
                _hub.Publish("approval.resolved", new
                {
                    id = Guid.NewGuid().ToString("N")[..12],
                    approved = true,
                    resolvedBy = "trust",
                    command,
                    agentName
                });
                return true;
            }

            string? escalationDetail = null;

            if (caller?.ManagerAgentId != null && _settings.JudgeEnabled)
            {
                var taskDescription = await FindCurrentTaskDescriptionAsync(caller.ManagerAgentId);
                var verdict = await _judge.JudgeAsync(new JudgeRequest(
                    command, workingDirectory, caller.AgentName,
                    AgentPurpose: null, TaskDescription: taskDescription));

                _hub.Publish("approval.judged", new
                {
                    command,
                    agentName,
                    decision = verdict.Decision.ToString().ToLowerInvariant(),
                    reason = verdict.Reason
                });

                switch (verdict.Decision)
                {
                    case JudgeDecision.Approve:
                        _hub.Publish("approval.resolved", new
                        {
                            id = Guid.NewGuid().ToString("N")[..12],
                            approved = true,
                            resolvedBy = "judge",
                            command,
                            agentName,
                            reason = verdict.Reason
                        });
                        return true;
                    case JudgeDecision.Deny:
                        _hub.Publish("approval.resolved", new
                        {
                            id = Guid.NewGuid().ToString("N")[..12],
                            approved = false,
                            resolvedBy = "judge",
                            command,
                            agentName,
                            reason = verdict.Reason
                        });
                        return false;
                    default:
                        escalationDetail = $"Escalated by judge: {verdict.Reason}";
                        break;
                }
            }

            return await _userQueue.RequestCommandApprovalAsync(command, workingDirectory, agentName, escalationDetail);
        }

        public string RequestTaskClaimApproval(SaturnTask task, Action<bool> onResolved)
        {
            var item = new PendingApprovalItem
            {
                Type = "task_claim",
                Title = $"Orchestrator wants to take: {task.Title}",
                Detail = task.Notes,
                TaskId = task.Id,
                AgentName = "orchestrator"
            };
            return _userQueue.RequestDecision(item, onResolved);
        }

        private async Task<string?> FindCurrentTaskDescriptionAsync(string agentManagerId)
        {
            try
            {
                var open = await _store.Project.GetOpenDispatchesAsync();
                var dispatch = open.FirstOrDefault(d => d.AgentId == agentManagerId);
                if (dispatch == null)
                {
                    return null;
                }
                var task = await _store.FindAsync(dispatch.TaskId);
                return task == null ? null : $"{task.Title}: {task.Notes}";
            }
            catch
            {
                return null;
            }
        }
    }
}

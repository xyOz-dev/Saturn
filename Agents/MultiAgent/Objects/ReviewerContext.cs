using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Saturn.Agents.MultiAgent.Objects
{
    public class ReviewerContext
    {
        public string Id { get; set; } = "";
        public Agent ReviewerAgent { get; set; } = null!;
        public string SubAgentId { get; set; } = "";
        public string TaskId { get; set; } = "";
        public int RevisionCount { get; set; } = 0;
        public DateTime StartedAt { get; set; }
        public TaskCompletionSource<ReviewDecision> DecisionSource { get; set; } = new();
    }
}

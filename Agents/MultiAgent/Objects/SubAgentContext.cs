using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Saturn.Agents.MultiAgent.Objects
{
    public class SubAgentContext
    {
        public string Id { get; set; } = "";
        public Agent Agent { get; set; } = null!;
        public string Name { get; set; } = "";
        public string Purpose { get; set; } = "";
        public AgentStatus Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public AgentTask? CurrentTask { get; set; }
        public int RevisionCount { get; set; } = 0;
    }
}

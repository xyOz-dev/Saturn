using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Saturn.Agents.MultiAgent.Objects
{
    public class AgentTask
    {
        public string Id { get; set; } = "";
        public string Description { get; set; } = "";
        public Dictionary<string, object>? Context { get; set; }
        public DateTime StartedAt { get; set; }
        public TaskStatus Status { get; set; }
    }
}

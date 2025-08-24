using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Saturn.Agents.MultiAgent.Objects
{
    public class AgentTaskResult
    {
        public string TaskId { get; set; } = "";
        public string AgentId { get; set; } = "";
        public string AgentName { get; set; } = "";
        public bool Success { get; set; }
        public string Result { get; set; } = "";
        public DateTime CompletedAt { get; set; }
        public TimeSpan Duration { get; set; }
    }
}

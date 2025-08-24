using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Saturn.Agents.MultiAgent.Objects
{
    public class AgentStatusInfo
    {
        public string AgentId { get; set; } = "";
        public string Name { get; set; } = "";
        public string Status { get; set; } = "";
        public string? CurrentTask { get; set; }
        public string? TaskId { get; set; }
        public bool IsIdle { get; set; }
        public bool Exists { get; set; }
        public TimeSpan RunningTime { get; set; }
    }
}

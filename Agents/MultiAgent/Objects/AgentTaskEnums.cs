using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Saturn.Agents.MultiAgent
{
    public enum AgentStatus
    {
        Idle,
        Working,
        BeingReviewed,
        Revising,
        Error,
        Terminated
    }

    public enum TaskStatus
    {
        Pending,
        Running,
        Completed,
        Failed
    }

    public enum ReviewStatus
    {
        Pending,
        Approved,
        RevisionRequested,
        Rejected
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Saturn.Agents.MultiAgent.Objects
{
    public class ReviewDecision
    {
        public ReviewStatus Status { get; set; }
        public string Feedback { get; set; } = "";
        public List<string> RevisionRequests { get; set; } = new();
    }
}

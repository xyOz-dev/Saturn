using OpenRouterSharp.Models.Requests;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Saturn.Agents.Core
{
    public abstract class AgentBase
    {
        public string Id { get; } = Guid.NewGuid().ToString();
        public AgentConfiguration Configuration { get; protected set; }
        public List<Message> ChatHistory { get; protected set; }

        public string Name => Configuration.Name;
        public string SystemPrompt => Configuration.SystemPrompt;

        protected AgentBase(AgentConfiguration configuration)
        {
            Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            ChatHistory = new List<Message>();
        }

        public abstract Task<T> Execute<T>(object input);

        public virtual void ClearHistory()
        {
            ChatHistory.Clear();
        }

        public virtual List<Message> GetHistory()
        {
            return new List<Message>(ChatHistory);
        }

        protected virtual void TrimHistory()
        {
            if (Configuration.MaxHistoryMessages.HasValue && ChatHistory.Count > Configuration.MaxHistoryMessages.Value)
            {
                var systemMessage = ChatHistory.FirstOrDefault(m => m.Role == "system");
                var messagesToKeep = Configuration.MaxHistoryMessages.Value - (systemMessage != null ? 1 : 0);
                
                var trimmedHistory = ChatHistory
                    .Where(m => m.Role != "system")
                    .TakeLast(messagesToKeep)
                    .ToList();

                ChatHistory.Clear();
                if (systemMessage != null)
                {
                    ChatHistory.Add(systemMessage);
                }
                ChatHistory.AddRange(trimmedHistory);
            }
        }
    }
}

using OpenRouterSharp.Models.Requests;
using Saturn.Agents.Core;
using Saturn.Services;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Saturn.Agents
{
    public class DefaultAgent : AgentBase
    {
        public DefaultAgent(AgentConfiguration configuration) : base(configuration)
        {
            if (Configuration.MaintainHistory && !ChatHistory.Any(m => m.Role == "system"))
            {
                var systemPrompt = BuildSystemPrompt();
                ChatHistory.Add(new Message { Role = "system", Content = systemPrompt });
            }
        }
        
        private string BuildSystemPrompt()
        {
            var sb = new StringBuilder(Configuration.SystemPrompt);
            
            if (Configuration.DynamicPromptProvider != null)
            {
                sb.AppendLine().AppendLine(Configuration.DynamicPromptProvider());
            }
            else if (Configuration.IncludeWorkspaceTree)
            {
                var tree = WorkspaceManager.Instance.GetWorkspaceTreeAsync(3).Result;
                if (tree != null)
                {
                    sb.AppendLine().AppendLine("Current Workspace Structure:");
                    sb.AppendLine("```");
                    sb.Append(WorkspaceManager.Instance.GenerateTreeString(tree));
                    sb.AppendLine("```");
                }
            }
            
            return sb.ToString();
        }

        public override async Task<T> Execute<T>(object input)
        {
            var userMessage = new Message { Role = "user", Content = input.ToString() };
            
            List<Message> messagesToSend;
            
            if (Configuration.MaintainHistory)
            {
                ChatHistory.Add(userMessage);
                TrimHistory();
                messagesToSend = new List<Message>(ChatHistory);
            }
            else
            {
                messagesToSend = new List<Message>
                {
                    new Message { Role = "system", Content = Configuration.SystemPrompt },
                    userMessage
                };
            }

            var responseMessage = await ExecuteWithTools(messagesToSend);
            
            Message finalMessage = null;
            if (responseMessage != null)
            {
                finalMessage = new Message 
                { 
                    Role = responseMessage.Role ?? "assistant",
                    Content = responseMessage.Content?.ToString() ?? ""
                };
                
                if (Configuration.MaintainHistory)
                {
                    ChatHistory.Add(finalMessage);
                }
            }

            return (T)(object)(finalMessage ?? new Message { Role = "assistant", Content = "I'm sorry, I couldn't process your request." });
        }
    }
}
using OpenRouterSharp;
using OpenRouterSharp.Models.Requests;
using Saturn.Agents.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Saturn.Agents
{
    public class DefaultAgent : AgentBase
    {
        public DefaultAgent(AgentConfiguration configuration) : base(configuration)
        {
            if (Configuration.MaintainHistory && !ChatHistory.Any(m => m.Role == "system"))
            {
                ChatHistory.Add(new Message { Role = "system", Content = Configuration.SystemPrompt });
            }
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

            ChatRequest chatRequest = new ChatRequest()
            {
                Model = Configuration.Model,
                Messages = messagesToSend,
                Temperature = Configuration.Temperature,
                MaxTokens = Configuration.MaxTokens,
                TopP = Configuration.TopP,
                FrequencyPenalty = Configuration.FrequencyPenalty,
                PresencePenalty = Configuration.PresencePenalty,
                Stop = Configuration.StopSequences
            };

            var response = await Configuration.Client.Chat.CreateCompletionAsync(chatRequest);
            var assistantMessage = response.Choices.FirstOrDefault()?.Message;

            if (Configuration.MaintainHistory && assistantMessage != null)
            {
                ChatHistory.Add(assistantMessage);
            }

            return (T)(object)assistantMessage!;
        }
    }
}

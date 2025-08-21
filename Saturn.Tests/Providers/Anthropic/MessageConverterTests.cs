using Xunit;
using FluentAssertions;
using Saturn.Providers.Models;
using SaturnFork.Providers.Anthropic.Utils;
using SaturnFork.Providers.Anthropic.Models;
using System.Collections.Generic;
using System.Text.Json;
using System.Linq;

namespace Saturn.Tests.Providers.Anthropic
{
    public class MessageConverterTests
    {
        [Fact]
        public void ConvertToAnthropicRequest_ExtractsSystemMessage()
        {
            // Arrange
            var request = new ChatRequest
            {
                Messages = new List<ChatMessage>
                {
                    new() { Role = "system", Content = "You are helpful" },
                    new() { Role = "user", Content = "Hello" }
                },
                Model = "claude-3-sonnet"
            };
            
            // Act
            var result = MessageConverter.ConvertToAnthropicRequest(request);
            
            // Assert
            result.System.Should().Be("You are helpful");
            result.Messages.Should().HaveCount(1);
            result.Messages[0].Role.Should().Be("user");
        }
        
        [Theory]
        [InlineData("claude-3-opus", "claude-3-opus-20240229")]
        [InlineData("claude-3-sonnet", "claude-3-5-sonnet-20241022")]
        [InlineData("claude-3-haiku", "claude-3-haiku-20240307")]
        [InlineData("claude-sonnet-4", "claude-sonnet-4-20250514")]
        [InlineData("anthropic/claude-sonnet-4", "claude-sonnet-4-20250514")]
        [InlineData("unknown-model", "unknown-model")]
        public void ConvertModelName_MapsCorrectly(string input, string expected)
        {
            // Arrange
            var request = new ChatRequest { Model = input };
            
            // Act
            var result = MessageConverter.ConvertToAnthropicRequest(request);
            
            // Assert
            result.Model.Should().Be(expected);
        }
        
        [Fact]
        public void ConvertMessage_HandlesToolCalls()
        {
            // Arrange
            var message = new ChatMessage
            {
                Role = "assistant",
                Content = "Let me help",
                ToolCalls = new List<ToolCall>
                {
                    new()
                    {
                        Id = "call_123",
                        Name = "get_weather",
                        Arguments = "{\"location\":\"NYC\"}"
                    }
                }
            };
            
            var request = new ChatRequest
            {
                Messages = new List<ChatMessage> { message }
            };
            
            // Act
            var result = MessageConverter.ConvertToAnthropicRequest(request);
            
            // Assert
            var anthropicMessage = result.Messages[0];
            var contentBlocks = anthropicMessage.Content as List<ContentBlock>;
            contentBlocks.Should().HaveCount(2);
            contentBlocks[0].Type.Should().Be("text");
            contentBlocks[0].Text.Should().Be("Let me help");
            contentBlocks[1].Type.Should().Be("tool_use");
            contentBlocks[1].Name.Should().Be("get_weather");
            contentBlocks[1].Id.Should().Be("call_123");
        }
        
        [Fact]
        public void ConvertTools_TransformsCorrectly()
        {
            // Arrange
            var tools = new List<ToolDefinition>
            {
                new()
                {
                    Name = "calculator",
                    Description = "Performs calculations",
                    Parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            expression = new { type = "string" }
                        }
                    }
                }
            };
            
            var request = new ChatRequest { Tools = tools };
            
            // Act
            var result = MessageConverter.ConvertToAnthropicRequest(request);
            
            // Assert
            result.Tools.Should().HaveCount(1);
            result.Tools[0].Name.Should().Be("calculator");
            result.Tools[0].Description.Should().Be("Performs calculations");
            result.Tools[0].InputSchema.Should().NotBeNull();
        }
        
        [Fact]
        public void ConvertFromAnthropicResponse_MapsUsageCorrectly()
        {
            // Arrange
            var anthropicResponse = new AnthropicChatResponse
            {
                Id = "msg_123",
                Model = "claude-3-sonnet",
                Usage = new AnthropicUsage
                {
                    InputTokens = 100,
                    OutputTokens = 50
                }
            };
            
            // Act
            var result = MessageConverter.ConvertFromAnthropicResponse(anthropicResponse);
            
            // Assert
            result.Usage.Should().NotBeNull();
            result.Usage.InputTokens.Should().Be(100);
            result.Usage.OutputTokens.Should().Be(50);
            result.Usage.TotalTokens.Should().Be(150);
        }
        
        [Fact]
        public void ConvertToAnthropicRequest_SetsDefaultMaxTokens()
        {
            // Arrange
            var request = new ChatRequest
            {
                Messages = new List<ChatMessage>
                {
                    new() { Role = "user", Content = "Hello" }
                },
                Model = "claude-3-sonnet"
            };
            
            // Act
            var result = MessageConverter.ConvertToAnthropicRequest(request);
            
            // Assert
            result.MaxTokens.Should().Be(4096);
        }
        
        [Fact]
        public void ConvertToAnthropicRequest_PreservesParameters()
        {
            // Arrange
            var request = new ChatRequest
            {
                Messages = new List<ChatMessage>
                {
                    new() { Role = "user", Content = "Hello" }
                },
                Model = "claude-3-sonnet",
                Temperature = 0.7,
                TopP = 0.9,
                MaxTokens = 1000,
                Stream = true
            };
            
            // Act
            var result = MessageConverter.ConvertToAnthropicRequest(request);
            
            // Assert
            result.Temperature.Should().Be(0.7);
            result.TopP.Should().Be(0.9);
            result.MaxTokens.Should().Be(1000);
            result.Stream.Should().BeTrue();
        }
        
        [Fact]
        public void ConvertMessage_HandlesTextOnlyMessage()
        {
            // Arrange
            var message = new ChatMessage
            {
                Role = "user",
                Content = "What is the weather like?"
            };
            
            var request = new ChatRequest
            {
                Messages = new List<ChatMessage> { message }
            };
            
            // Act
            var result = MessageConverter.ConvertToAnthropicRequest(request);
            
            // Assert
            var anthropicMessage = result.Messages[0];
            anthropicMessage.Role.Should().Be("user");
            anthropicMessage.Content.Should().Be("What is the weather like?");
        }
        
        [Fact]
        public void ConvertMessage_HandlesToolResult()
        {
            // Arrange
            var message = new ChatMessage
            {
                Role = "user",
                Content = "Weather data: sunny, 72F",
                ToolCallId = "call_123"
            };
            
            var request = new ChatRequest
            {
                Messages = new List<ChatMessage> { message }
            };
            
            // Act
            var result = MessageConverter.ConvertToAnthropicRequest(request);
            
            // Assert
            var anthropicMessage = result.Messages[0];
            var contentBlocks = anthropicMessage.Content as List<ContentBlock>;
            contentBlocks.Should().HaveCount(1);
            contentBlocks[0].Type.Should().Be("tool_result");
            contentBlocks[0].ToolUseId.Should().Be("call_123");
            contentBlocks[0].ToolResultContent.Should().Be("Weather data: sunny, 72F");
        }
        
        [Fact]
        public void ConvertFromAnthropicResponse_HandlesTextContent()
        {
            // Arrange
            var anthropicResponse = new AnthropicChatResponse
            {
                Id = "msg_123",
                Model = "claude-3-sonnet",
                StopReason = "end_turn",
                Content = new List<ContentBlock>
                {
                    new() { Type = "text", Text = "Hello! How can I help you?" }
                }
            };
            
            // Act
            var result = MessageConverter.ConvertFromAnthropicResponse(anthropicResponse);
            
            // Assert
            result.Id.Should().Be("msg_123");
            result.Model.Should().Be("claude-3-sonnet");
            result.FinishReason.Should().Be("end_turn");
            result.Message.Role.Should().Be("assistant");
            result.Message.Content.Should().Be("Hello! How can I help you?");
        }
        
        [Fact]
        public void ConvertFromAnthropicResponse_HandlesToolUseContent()
        {
            // Arrange
            var anthropicResponse = new AnthropicChatResponse
            {
                Id = "msg_123",
                Model = "claude-3-sonnet",
                Content = new List<ContentBlock>
                {
                    new() { Type = "text", Text = "Let me check the weather." },
                    new() 
                    { 
                        Type = "tool_use", 
                        Id = "tool_123",
                        Name = "get_weather",
                        Input = new { location = "NYC" }
                    }
                }
            };
            
            // Act
            var result = MessageConverter.ConvertFromAnthropicResponse(anthropicResponse);
            
            // Assert
            result.Message.Content.Should().Be("Let me check the weather.");
            result.Message.ToolCalls.Should().HaveCount(1);
            result.Message.ToolCalls[0].Id.Should().Be("tool_123");
            result.Message.ToolCalls[0].Name.Should().Be("get_weather");
            result.Message.ToolCalls[0].Arguments.Should().NotBeNullOrEmpty();
            // Verify the arguments can be parsed as JSON and contain expected data
            var argumentsJson = JsonSerializer.Deserialize<JsonElement>(result.Message.ToolCalls[0].Arguments);
            argumentsJson.TryGetProperty("location", out var location).Should().BeTrue();
            location.GetString().Should().Be("NYC");
        }
        
        [Fact]
        public void ConvertToAnthropicRequest_HandlesEmptyTools()
        {
            // Arrange
            var request = new ChatRequest
            {
                Messages = new List<ChatMessage>
                {
                    new() { Role = "user", Content = "Hello" }
                },
                Tools = new List<ToolDefinition>()
            };
            
            // Act
            var result = MessageConverter.ConvertToAnthropicRequest(request);
            
            // Assert
            result.Tools.Should().BeNull();
        }
        
        [Fact]
        public void ConvertToAnthropicRequest_HandlesNullTools()
        {
            // Arrange
            var request = new ChatRequest
            {
                Messages = new List<ChatMessage>
                {
                    new() { Role = "user", Content = "Hello" }
                },
                Tools = null
            };
            
            // Act
            var result = MessageConverter.ConvertToAnthropicRequest(request);
            
            // Assert
            result.Tools.Should().BeNull();
        }
        
        [Fact]
        public void ConvertFromAnthropicResponse_HandlesNullUsage()
        {
            // Arrange
            var anthropicResponse = new AnthropicChatResponse
            {
                Id = "msg_123",
                Model = "claude-3-sonnet",
                Usage = null
            };
            
            // Act
            var result = MessageConverter.ConvertFromAnthropicResponse(anthropicResponse);
            
            // Assert
            result.Usage.Should().BeNull();
        }
        
        [Fact]
        public void ConvertMessage_MapsAssistantRole()
        {
            // Arrange
            var message = new ChatMessage
            {
                Role = "assistant",
                Content = "I'm an assistant"
            };
            
            var request = new ChatRequest
            {
                Messages = new List<ChatMessage> { message }
            };
            
            // Act
            var result = MessageConverter.ConvertToAnthropicRequest(request);
            
            // Assert
            result.Messages[0].Role.Should().Be("assistant");
        }
    }
}
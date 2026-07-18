using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Saturn.Agents.Core;
using Saturn.OpenRouter.Models.Api.Chat;
using Saturn.Providers;
using Saturn.Skills;
using Saturn.Tests.Providers;
using Saturn.Tools;
using Saturn.Tools.Core;
using Xunit;

namespace Saturn.Tests.Skills
{
    [Collection("Configuration")]
    public class SkillInjectionTests : SkillTestBase
    {
        private static TestAgent CreateAgent(
            SkillAudience audience,
            string? subAgentTypeName = null,
            bool enableSkills = true,
            int? maxHistoryMessages = 200)
        {
            var config = new AgentConfiguration
            {
                Name = "Test",
                SystemPrompt = "You are a test agent.",
                ClientSource = new StaticClientSource(new FakeLlmClient(), "test-provider"),
                Model = "test-model",
                MaintainHistory = true,
                MaxHistoryMessages = maxHistoryMessages,
                EnableSkills = enableSkills,
                SkillAudience = audience,
                SubAgentTypeName = subAgentTypeName
            };
            return new TestAgent(config);
        }

        private static string ContentOf(Message message)
        {
            return message.Content.ValueKind == JsonValueKind.String
                ? message.Content.GetString() ?? string.Empty
                : message.Content.GetRawText();
        }

        private static List<Message> EnvelopesIn(TestAgent agent)
        {
            return agent.ChatHistory
                .Where(m => m.Role == "user" && SkillEnvelope.TryExtractName(ContentOf(m)) != null)
                .ToList();
        }

        [Fact]
        public async Task Execute_MatchingMessage_InjectsSkillAfterUserMessage()
        {
            await SkillManager.CreateSkillAsync(NewSkill("release-checklist", "Steps for cutting a release.", "release checklist", "changelog"));

            var agent = CreateAgent(SkillAudience.Orchestrator);
            var fired = new List<string>();
            agent.OnSkillInjected += (name, envelope) => fired.Add(name);

            await agent.Execute<Message>("How do I run the release checklist?");

            var envelopes = EnvelopesIn(agent);
            envelopes.Should().ContainSingle();
            var content = ContentOf(envelopes[0]);
            content.Should().StartWith("<injected-skill name=\"release-checklist\">");
            content.Should().Contain("Steps for cutting a release.");

            // The envelope sits directly after the triggering user message.
            var userIndex = agent.ChatHistory.FindIndex(m => m.Role == "user" && ContentOf(m).Contains("How do I run"));
            agent.ChatHistory[userIndex + 1].Should().BeSameAs(envelopes[0]);

            fired.Should().BeEquivalentTo(new[] { "release-checklist" });
        }

        [Fact]
        public async Task Execute_SecondMatch_DoesNotInjectTwice()
        {
            await SkillManager.CreateSkillAsync(NewSkill("lockfile-skill", "content", "lockfile"));
            var agent = CreateAgent(SkillAudience.Orchestrator);

            await agent.Execute<Message>("open the lockfile");
            await agent.Execute<Message>("read the lockfile again");

            EnvelopesIn(agent).Should().ContainSingle();
        }

        [Fact]
        public async Task Execute_ReinjectsAfterEnvelopeLeavesContext()
        {
            await SkillManager.CreateSkillAsync(NewSkill("lockfile-skill", "content", "lockfile"));
            var agent = CreateAgent(SkillAudience.Orchestrator);

            await agent.Execute<Message>("open the lockfile");
            EnvelopesIn(agent).Should().ContainSingle();

            // Simulate the envelope being trimmed out of the context window.
            agent.ChatHistory.Remove(EnvelopesIn(agent)[0]);

            await agent.Execute<Message>("check the lockfile once more");
            EnvelopesIn(agent).Should().ContainSingle();
        }

        [Fact]
        public async Task Execute_NonMatchingMessage_InjectsNothing()
        {
            await SkillManager.CreateSkillAsync(NewSkill("lockfile-skill", "content", "lockfile"));
            var agent = CreateAgent(SkillAudience.Orchestrator);

            await agent.Execute<Message>("write me a haiku");

            EnvelopesIn(agent).Should().BeEmpty();
        }

        [Fact]
        public async Task Execute_SkillsDisabledOrNoAudience_InjectsNothing()
        {
            await SkillManager.CreateSkillAsync(NewSkill("lockfile-skill", "content", "lockfile"));

            var disabledAgent = CreateAgent(SkillAudience.Orchestrator, enableSkills: false);
            await disabledAgent.Execute<Message>("open the lockfile");
            EnvelopesIn(disabledAgent).Should().BeEmpty();

            var noAudienceAgent = CreateAgent(SkillAudience.None);
            await noAudienceAgent.Execute<Message>("open the lockfile");
            EnvelopesIn(noAudienceAgent).Should().BeEmpty();
        }

        [Fact]
        public async Task Execute_SubAgentTypeTargeting_IsRespected()
        {
            var skill = NewSkill("coder-skill", "content", "lockfile");
            skill.SubAgentTypes = new List<string> { "coder" };
            await SkillManager.CreateSkillAsync(skill);

            var coder = CreateAgent(SkillAudience.SubAgent, subAgentTypeName: "coder");
            await coder.Execute<Message>("open the lockfile");
            EnvelopesIn(coder).Should().ContainSingle();

            var explorer = CreateAgent(SkillAudience.SubAgent, subAgentTypeName: "explorer");
            await explorer.Execute<Message>("open the lockfile");
            EnvelopesIn(explorer).Should().BeEmpty();
        }

        [Fact]
        public async Task Execute_OrchestratorOnlySkill_NotInjectedIntoSubAgents()
        {
            var skill = NewSkill("orch-skill", "content", "lockfile");
            skill.ApplyToSubAgents = false;
            await SkillManager.CreateSkillAsync(skill);

            var subAgent = CreateAgent(SkillAudience.SubAgent, subAgentTypeName: "coder");
            await subAgent.Execute<Message>("open the lockfile");

            EnvelopesIn(subAgent).Should().BeEmpty();
        }
    }

    [Collection("Configuration")]
    public class LoadSkillToolTests : SkillTestBase
    {
        private static TestAgent CreateAgent(SkillAudience audience, string? subAgentTypeName = null)
        {
            var config = new AgentConfiguration
            {
                Name = "Test",
                SystemPrompt = "You are a test agent.",
                ClientSource = new StaticClientSource(new FakeLlmClient(), "test-provider"),
                Model = "test-model",
                MaintainHistory = true,
                SkillAudience = audience,
                SubAgentTypeName = subAgentTypeName
            };
            return new TestAgent(config);
        }

        private static void SetContext(TestAgent agent)
        {
            AgentContext.Current = new AgentExecutionContext
            {
                Configuration = agent.Configuration,
                AgentName = agent.Name,
                Agent = agent
            };
        }

        public override void Dispose()
        {
            AgentContext.Current = null;
            base.Dispose();
        }

        [Fact]
        public void Schema_RequiresName()
        {
            var tool = new LoadSkillTool();
            var schema = tool.GetParameters();

            ((string[])schema["required"]).Should().BeEquivalentTo(new[] { "name" });
            ((Dictionary<string, object>)schema["properties"]).Keys.Should().Contain("name");
        }

        [Fact]
        public async Task Description_ListsLiveCatalog()
        {
            await SkillManager.CreateSkillAsync(NewSkill("catalog-entry"));

            new LoadSkillTool().Description.Should().Contain("catalog-entry");
        }

        [Fact]
        public async Task Execute_KnownSkill_ReturnsEnvelope()
        {
            await SkillManager.CreateSkillAsync(NewSkill("wanted-skill", "the payload"));
            SetContext(CreateAgent(SkillAudience.Orchestrator));

            var result = await new LoadSkillTool().ExecuteAsync(new Dictionary<string, object> { ["name"] = "wanted-skill" });

            result.Success.Should().BeTrue();
            result.FormattedOutput.Should().StartWith("<injected-skill name=\"wanted-skill\">");
            result.FormattedOutput.Should().Contain("the payload");
            result.FormattedOutput.Should().Contain("load_skill");
        }

        [Fact]
        public async Task Execute_UnknownOrDisabledSkill_ReturnsError()
        {
            var disabled = NewSkill("switched-off");
            disabled.Enabled = false;
            await SkillManager.CreateSkillAsync(disabled);
            SetContext(CreateAgent(SkillAudience.Orchestrator));

            var tool = new LoadSkillTool();

            (await tool.ExecuteAsync(new Dictionary<string, object> { ["name"] = "ghost" }))
                .Success.Should().BeFalse();
            (await tool.ExecuteAsync(new Dictionary<string, object> { ["name"] = "switched-off" }))
                .Success.Should().BeFalse();
        }

        [Fact]
        public async Task Execute_SkillNotTargetedAtAgent_ReturnsError()
        {
            var orchOnly = NewSkill("orch-only-skill");
            orchOnly.ApplyToSubAgents = false;
            await SkillManager.CreateSkillAsync(orchOnly);
            SetContext(CreateAgent(SkillAudience.SubAgent, subAgentTypeName: "coder"));

            var result = await new LoadSkillTool().ExecuteAsync(new Dictionary<string, object> { ["name"] = "orch-only-skill" });

            result.Success.Should().BeFalse();
            result.Error.Should().Contain("not available");
        }

        [Fact]
        public async Task Execute_AlreadyInjectedSkill_ReportsAlreadyLoaded()
        {
            var skill = await SkillManager.CreateSkillAsync(NewSkill("dup-skill", "payload"));
            var agent = CreateAgent(SkillAudience.Orchestrator);
            agent.ChatHistory.Add(new Message
            {
                Role = "user",
                Content = JsonDocument.Parse(JsonSerializer.Serialize(SkillEnvelope.Build(skill, false))).RootElement
            });
            SetContext(agent);

            var result = await new LoadSkillTool().ExecuteAsync(new Dictionary<string, object> { ["name"] = "dup-skill" });

            result.Success.Should().BeTrue();
            result.FormattedOutput.Should().Contain("already loaded");
            result.FormattedOutput.Should().NotContain("payload");
        }

        [Fact]
        public async Task Execute_EmptyName_ReturnsError()
        {
            SetContext(CreateAgent(SkillAudience.Orchestrator));

            var result = await new LoadSkillTool().ExecuteAsync(new Dictionary<string, object> { ["name"] = "  " });

            result.Success.Should().BeFalse();
        }

        [Fact]
        public async Task Execute_FailsClosedWithoutAValidAudience()
        {
            await SkillManager.CreateSkillAsync(NewSkill("guarded-skill"));
            var tool = new LoadSkillTool();
            var request = new Dictionary<string, object> { ["name"] = "guarded-skill" };

            AgentContext.Current = null;
            (await tool.ExecuteAsync(request)).Success.Should().BeFalse();

            SetContext(CreateAgent(SkillAudience.None));
            var result = await tool.ExecuteAsync(request);
            result.Success.Should().BeFalse();
            result.Error.Should().Contain("not enabled");
        }
    }
}

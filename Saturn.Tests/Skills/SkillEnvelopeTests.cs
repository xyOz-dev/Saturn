using System.Collections.Generic;
using System.Text.Json;
using FluentAssertions;
using Saturn.OpenRouter.Models.Api.Chat;
using Saturn.Skills;
using Xunit;

namespace Saturn.Tests.Skills
{
    public class SkillEnvelopeTests
    {
        private static JsonElement JStr(string value)
        {
            return JsonDocument.Parse(JsonSerializer.Serialize(value)).RootElement;
        }

        private static Skill MakeSkill(string name, string content = "Skill body.")
        {
            return new Skill { Name = name, Content = content };
        }

        [Fact]
        public void Build_AutoInjected_ContainsMarkerContentAndTriggerWording()
        {
            var envelope = SkillEnvelope.Build(MakeSkill("valorant-lockfile", "The lockfile lives in AppData."), requestedByModel: false);

            envelope.Should().StartWith("<injected-skill name=\"valorant-lockfile\">");
            envelope.Should().Contain("matched its triggers");
            envelope.Should().Contain("The lockfile lives in AppData.");
            envelope.Should().EndWith("</injected-skill>");
        }

        [Fact]
        public void Build_RequestedByModel_ContainsLoadSkillWording()
        {
            var envelope = SkillEnvelope.Build(MakeSkill("valorant-lockfile"), requestedByModel: true);

            envelope.Should().Contain("load_skill");
            envelope.Should().NotContain("matched its triggers");
        }

        [Fact]
        public void TryExtractName_RoundTripsThroughBuild()
        {
            var envelope = SkillEnvelope.Build(MakeSkill("my skill_2.0"), requestedByModel: false);

            SkillEnvelope.TryExtractName(envelope).Should().Be("my skill_2.0");
        }

        [Fact]
        public void TryExtractName_EscapedCharactersRoundTrip()
        {
            var envelope = SkillEnvelope.Build(MakeSkill("a&b\"c"), requestedByModel: false);

            SkillEnvelope.TryExtractName(envelope).Should().Be("a&b\"c");
        }

        [Fact]
        public void TryExtractName_OrdinaryText_ReturnsNull()
        {
            SkillEnvelope.TryExtractName("just a normal message").Should().BeNull();
            SkillEnvelope.TryExtractName("").Should().BeNull();
            SkillEnvelope.TryExtractName(null).Should().BeNull();
            SkillEnvelope.TryExtractName("prefix <injected-skill name=\"x\"> not at start").Should().BeNull();
        }

        [Fact]
        public void FindInjectedSkillNames_ScansUserAndToolMessages()
        {
            var messages = new List<Message>
            {
                new Message { Role = "system", Content = JStr("system prompt") },
                new Message { Role = "user", Content = JStr("normal user message") },
                new Message { Role = "user", Content = JStr(SkillEnvelope.Build(MakeSkill("auto-skill"), false)) },
                new Message { Role = "tool", Content = JStr(SkillEnvelope.Build(MakeSkill("tool-skill"), true)), ToolCallId = "t1" },
                new Message { Role = "assistant", Content = JStr(SkillEnvelope.Build(MakeSkill("assistant-echo"), false)) }
            };

            var names = SkillEnvelope.FindInjectedSkillNames(messages);

            names.Should().BeEquivalentTo(new[] { "auto-skill", "tool-skill" });
        }

        [Fact]
        public void FindInjectedSkillNames_IsCaseInsensitive()
        {
            var messages = new List<Message>
            {
                new Message { Role = "user", Content = JStr(SkillEnvelope.Build(MakeSkill("My-Skill"), false)) }
            };

            SkillEnvelope.FindInjectedSkillNames(messages).Contains("my-skill").Should().BeTrue();
        }

        [Fact]
        public void FindInjectedSkillNames_ReadsArrayContentParts()
        {
            // Cached messages carry content as an array of text parts.
            var envelope = SkillEnvelope.Build(MakeSkill("cached-skill"), false);
            var arrayContent = JsonDocument.Parse(JsonSerializer.Serialize(new[]
            {
                new { type = "text", text = envelope }
            })).RootElement;

            var messages = new List<Message>
            {
                new Message { Role = "user", Content = arrayContent }
            };

            SkillEnvelope.FindInjectedSkillNames(messages).Should().BeEquivalentTo(new[] { "cached-skill" });
        }
    }
}

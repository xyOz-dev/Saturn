using System.Collections.Generic;
using FluentAssertions;
using Saturn.Skills;
using Xunit;

namespace Saturn.Tests.Skills
{
    public class SkillMatcherTests
    {
        private static Skill MakeSkill(string name, params string[] triggers)
        {
            return new Skill
            {
                Name = name,
                Content = "content",
                Triggers = new List<string>(triggers)
            };
        }

        [Theory]
        [InlineData("How do I handle the api rate limit?")]
        [InlineData("respect the API RATE LIMIT")]
        [InlineData("the api rate-limit, please")]
        [InlineData("api_rate_limit")]
        public void Matches_TriggerPhrase_AcrossCaseAndPunctuation(string input)
        {
            var skill = MakeSkill("some-skill", "api rate limit");

            SkillMatcher.Matches(skill, input).Should().BeTrue();
        }

        [Fact]
        public void Matches_SkillName_IsAnImplicitTrigger()
        {
            var skill = MakeSkill("rate-limit-handling");

            SkillMatcher.Matches(skill, "use the rate limit handling approach").Should().BeTrue();
            SkillMatcher.Matches(skill, "use Rate-Limit-Handling now").Should().BeTrue();
        }

        [Fact]
        public void Matches_RequiresWholeWords()
        {
            var skill = MakeSkill("some-skill", "lock");

            SkillMatcher.Matches(skill, "the app blocked me").Should().BeFalse();
            SkillMatcher.Matches(skill, "there is a lockfile here").Should().BeFalse();
            SkillMatcher.Matches(skill, "please lock the door").Should().BeTrue();
        }

        [Fact]
        public void Matches_TriggerAtStartAndEndOfInput()
        {
            var skill = MakeSkill("some-skill", "lockfile");

            SkillMatcher.Matches(skill, "lockfile please").Should().BeTrue();
            SkillMatcher.Matches(skill, "read the lockfile").Should().BeTrue();
            SkillMatcher.Matches(skill, "lockfile").Should().BeTrue();
        }

        [Fact]
        public void Matches_EmptyOrIrrelevantInput_ReturnsFalse()
        {
            var skill = MakeSkill("some-skill", "lockfile");

            SkillMatcher.Matches(skill, "").Should().BeFalse();
            SkillMatcher.Matches(skill, "   ").Should().BeFalse();
            SkillMatcher.Matches(skill, "completely unrelated request").Should().BeFalse();
            SkillMatcher.Matches(null!, "lockfile").Should().BeFalse();
        }

        [Fact]
        public void Matches_PunctuationOnlyTrigger_NeverMatches()
        {
            var skill = MakeSkill("some-skill", "!!!");

            SkillMatcher.Matches(skill, "anything at all !!!").Should().BeFalse();
        }

        [Fact]
        public void Matches_PluralIsADifferentWord()
        {
            var skill = MakeSkill("some-skill", "lockfile");

            SkillMatcher.Matches(skill, "all the lockfiles").Should().BeFalse();
        }
    }
}

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
        [InlineData("How do I read the valorant lock file?")]
        [InlineData("interact with the VALORANT LOCK FILE")]
        [InlineData("the valorant lock-file, please")]
        [InlineData("valorant_lock_file")]
        public void Matches_TriggerPhrase_AcrossCaseAndPunctuation(string input)
        {
            var skill = MakeSkill("some-skill", "valorant lock file");

            SkillMatcher.Matches(skill, input).Should().BeTrue();
        }

        [Fact]
        public void Matches_SkillName_IsAnImplicitTrigger()
        {
            var skill = MakeSkill("valorant-lockfile");

            SkillMatcher.Matches(skill, "use the valorant lockfile approach").Should().BeTrue();
            SkillMatcher.Matches(skill, "use Valorant-Lockfile now").Should().BeTrue();
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

using System;
using System.Linq;
using System.Text;

namespace Saturn.Skills
{
    /// <summary>
    /// Deterministic trigger matching: a skill matches a message when any of its
    /// triggers (or its name) appears in the message as a whole word or phrase.
    /// Matching is case-insensitive and treats punctuation, hyphens, and
    /// underscores as word separators, so the trigger "api rate limit" matches
    /// "the API rate-limit" but "lock" does not match "blocked".
    /// </summary>
    public static class SkillMatcher
    {
        public static bool Matches(Skill skill, string input)
        {
            if (skill == null || string.IsNullOrWhiteSpace(input))
            {
                return false;
            }

            var normalizedInput = Normalize(input);
            if (normalizedInput.Length == 0)
            {
                return false;
            }

            var paddedInput = " " + normalizedInput + " ";

            return skill.Triggers
                .Concat(new[] { skill.Name })
                .Any(trigger => TriggerMatches(paddedInput, trigger));
        }

        private static bool TriggerMatches(string paddedInput, string? trigger)
        {
            if (string.IsNullOrWhiteSpace(trigger))
            {
                return false;
            }

            var normalizedTrigger = Normalize(trigger);
            if (normalizedTrigger.Length == 0)
            {
                return false;
            }

            return paddedInput.Contains(" " + normalizedTrigger + " ", StringComparison.Ordinal);
        }

        /// <summary>Lowercase, non-alphanumerics collapsed to single spaces.</summary>
        private static string Normalize(string text)
        {
            var builder = new StringBuilder(text.Length);
            var lastWasSpace = true;

            foreach (var c in text)
            {
                if (char.IsLetterOrDigit(c))
                {
                    builder.Append(char.ToLowerInvariant(c));
                    lastWasSpace = false;
                }
                else if (!lastWasSpace)
                {
                    builder.Append(' ');
                    lastWasSpace = true;
                }
            }

            return builder.ToString().TrimEnd();
        }
    }
}

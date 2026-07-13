using System;
using FluentAssertions;
using Saturn.Core.Tasks;
using Saturn.Data.Tasks;
using Xunit;

namespace Saturn.Tests.Tasks
{
    public class RecurrenceCalculatorTests
    {
        [Fact]
        public void Validate_NoneKind_IsValid()
        {
            RecurrenceCalculator.Validate(RecurrenceKinds.None, null, null).Should().BeNull();
        }

        [Theory]
        [InlineData(null)]
        [InlineData(0)]
        [InlineData(59)]
        public void Validate_IntervalBelowMinimum_IsRejected(int? seconds)
        {
            RecurrenceCalculator.Validate(RecurrenceKinds.Interval, seconds, null).Should().NotBeNull();
        }

        [Theory]
        [InlineData(60)]
        [InlineData(3600)]
        [InlineData(86400)]
        public void Validate_IntervalAtOrAboveMinimum_IsValid(int seconds)
        {
            RecurrenceCalculator.Validate(RecurrenceKinds.Interval, seconds, null).Should().BeNull();
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not a cron")]
        [InlineData("99 99 * * *")]
        public void Validate_MissingOrMalformedCron_IsRejected(string? cron)
        {
            RecurrenceCalculator.Validate(RecurrenceKinds.Cron, null, cron).Should().NotBeNull();
        }

        [Theory]
        [InlineData("0 9 * * *")]
        [InlineData("*/5 * * * *")]
        [InlineData("30 0 9 * * *")] // six-field (seconds) variant
        public void Validate_WellFormedCron_IsValid(string cron)
        {
            RecurrenceCalculator.Validate(RecurrenceKinds.Cron, null, cron).Should().BeNull();
        }

        [Fact]
        public void Validate_UnknownKind_IsRejected()
        {
            RecurrenceCalculator.Validate("fortnightly", null, null).Should().NotBeNull();
        }

        [Fact]
        public void Validate_CronThatNeverFires_IsRejected()
        {
            // February 30th never exists; syntax is fine but no occurrence can ever fire.
            RecurrenceCalculator.Validate(RecurrenceKinds.Cron, null, "0 0 30 2 *").Should().NotBeNull();
        }

        [Fact]
        public void GetNextOccurrenceUtc_CronThatNeverFires_ReturnsNullInsteadOfThrowing()
        {
            var next = RecurrenceCalculator.GetNextOccurrenceUtc(RecurrenceKinds.Cron, null, "0 0 30 2 *", DateTime.UtcNow);
            next.Should().BeNull();
        }

        [Fact]
        public void GetNextOccurrenceUtc_Interval_AddsTheInterval()
        {
            var after = new DateTime(2026, 7, 13, 10, 0, 0, DateTimeKind.Utc);
            var next = RecurrenceCalculator.GetNextOccurrenceUtc(RecurrenceKinds.Interval, 3600, null, after);
            next.Should().Be(after.AddHours(1));
        }

        [Fact]
        public void GetNextOccurrenceUtc_Cron_ReturnsFutureUtcMatchingLocalWallClock()
        {
            var after = DateTime.UtcNow;
            var next = RecurrenceCalculator.GetNextOccurrenceUtc(RecurrenceKinds.Cron, null, "0 9 * * *", after);

            next.Should().NotBeNull();
            next!.Value.Kind.Should().Be(DateTimeKind.Utc);
            next.Value.Should().BeAfter(after);
            // Cron expresses local wall-clock intent; 9am local, stored as UTC.
            next.Value.ToLocalTime().Hour.Should().Be(9);
            next.Value.ToLocalTime().Minute.Should().Be(0);
        }

        [Fact]
        public void GetNextOccurrenceUtc_NoneKind_ReturnsNull()
        {
            RecurrenceCalculator.GetNextOccurrenceUtc(RecurrenceKinds.None, null, null, DateTime.UtcNow).Should().BeNull();
        }

        [Fact]
        public void TryParseCron_SupportsFiveAndSixFieldExpressions()
        {
            RecurrenceCalculator.TryParseCron("0 9 * * *", out var fiveField).Should().BeTrue();
            fiveField.Should().NotBeNull();

            RecurrenceCalculator.TryParseCron("30 0 9 * * *", out var sixField).Should().BeTrue();
            sixField.Should().NotBeNull();

            RecurrenceCalculator.TryParseCron("nonsense", out var invalid).Should().BeFalse();
            invalid.Should().BeNull();
        }

        [Theory]
        [InlineData(86400, "every day")]
        [InlineData(172800, "every 2 days")]
        [InlineData(3600, "every hour")]
        [InlineData(7200, "every 2 hours")]
        [InlineData(300, "every 5 min")]
        public void Describe_Interval_ProducesReadableText(int seconds, string expected)
        {
            RecurrenceCalculator.Describe(RecurrenceKinds.Interval, seconds, null).Should().Be(expected);
        }

        [Fact]
        public void Describe_Cron_EchoesTheExpression()
        {
            RecurrenceCalculator.Describe(RecurrenceKinds.Cron, null, "0 9 * * *").Should().Be("cron: 0 9 * * *");
        }
    }
}

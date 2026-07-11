using System;
using FluentAssertions;
using Saturn.Providers;
using Xunit;

namespace Saturn.Tests.Providers
{
    public class ProviderRegistryTests
    {
        [Fact]
        public void Get_IsCaseInsensitive()
        {
            var name = "case-" + Guid.NewGuid().ToString("N");
            ProviderRegistry.Register(new FakeProvider { Name = name });

            ProviderRegistry.Get(name.ToUpperInvariant()).Name.Should().Be(name);
        }

        [Fact]
        public void Get_UnknownName_ThrowsWithAvailableProviders()
        {
            var registered = "known-" + Guid.NewGuid().ToString("N");
            ProviderRegistry.Register(new FakeProvider { Name = registered });

            var act = () => ProviderRegistry.Get("nope-" + Guid.NewGuid().ToString("N"));

            act.Should().Throw<InvalidOperationException>().WithMessage($"*{registered}*");
        }

        [Fact]
        public void Register_SameNameTwice_LastOneWins()
        {
            var name = "dup-" + Guid.NewGuid().ToString("N");
            ProviderRegistry.Register(new FakeProvider { Name = name, DisplayName = "First" });
            ProviderRegistry.Register(new FakeProvider { Name = name, DisplayName = "Second" });

            ProviderRegistry.Get(name).DisplayName.Should().Be("Second");
        }
    }
}

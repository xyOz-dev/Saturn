using System;
using System.Threading.Tasks;
using FluentAssertions;
using Saturn.Providers;
using Xunit;

namespace Saturn.Tests.Providers
{
    public class LlmClientManagerTests
    {
        [Fact]
        public async Task SwapAsync_UnknownProvider_FailsWithoutTouchingCurrent()
        {
            var manager = new LlmClientManager();

            var result = await manager.SwapAsync("does-not-exist-" + Guid.NewGuid().ToString("N"), new ProviderSettings());

            result.Success.Should().BeFalse();
            result.Error.Should().Contain("Unknown provider");
            manager.IsConnected.Should().BeFalse();
        }

        [Fact]
        public async Task SwapAsync_Success_SetsCurrentAndRaisesEvent()
        {
            var providerName = "swap-ok-" + Guid.NewGuid().ToString("N");
            var client = new FakeLlmClient();
            ProviderRegistry.Register(new FakeProvider { Name = providerName, Factory = _ => client });

            var manager = new LlmClientManager();
            ProviderChangedEventArgs? raised = null;
            manager.ProviderChanged += (_, e) => raised = e;

            var result = await manager.SwapAsync(providerName, new ProviderSettings());

            result.Success.Should().BeTrue();
            manager.IsConnected.Should().BeTrue();
            manager.Current.Should().BeSameAs(client);
            manager.ActiveProviderName.Should().Be(providerName);
            raised.Should().NotBeNull();
            raised!.NewProviderName.Should().Be(providerName);
        }

        [Fact]
        public async Task SwapAsync_ValidateFails_KeepsPreviousClientActive()
        {
            var goodName = "swap-good-" + Guid.NewGuid().ToString("N");
            var badName = "swap-bad-" + Guid.NewGuid().ToString("N");
            var goodClient = new FakeLlmClient();
            var badClient = new FakeLlmClient { ValidateResult = false };
            ProviderRegistry.Register(new FakeProvider { Name = goodName, Factory = _ => goodClient });
            ProviderRegistry.Register(new FakeProvider { Name = badName, Factory = _ => badClient });

            var manager = new LlmClientManager();
            (await manager.SwapAsync(goodName, new ProviderSettings())).Success.Should().BeTrue();

            var result = await manager.SwapAsync(badName, new ProviderSettings());

            result.Success.Should().BeFalse();
            manager.Current.Should().BeSameAs(goodClient);
            manager.ActiveProviderName.Should().Be(goodName);
            badClient.Disposed.Should().BeTrue();
            goodClient.Disposed.Should().BeFalse();
        }

        [Fact]
        public async Task SwapAsync_ProviderThrowsOnCreate_ReturnsErrorMessage()
        {
            var providerName = "swap-throws-" + Guid.NewGuid().ToString("N");
            ProviderRegistry.Register(new FakeProvider
            {
                Name = providerName,
                Factory = _ => throw new InvalidOperationException("missing api key")
            });

            var manager = new LlmClientManager();
            var result = await manager.SwapAsync(providerName, new ProviderSettings());

            result.Success.Should().BeFalse();
            result.Error.Should().Contain("missing api key");
        }

        [Fact]
        public async Task SwapAsync_ValidationNotRequired_InstallsUnreachableClient()
        {
            var providerName = "swap-novalidate-" + Guid.NewGuid().ToString("N");
            var client = new FakeLlmClient { ValidateResult = false };
            ProviderRegistry.Register(new FakeProvider { Name = providerName, Factory = _ => client });

            var manager = new LlmClientManager();
            var result = await manager.SwapAsync(providerName, new ProviderSettings(), requireValidation: false);

            result.Success.Should().BeTrue();
            manager.Current.Should().BeSameAs(client);
        }

        [Fact]
        public void Current_BeforeAnySwap_Throws()
        {
            var manager = new LlmClientManager();
            var act = () => manager.Current;
            act.Should().Throw<InvalidOperationException>();
        }
    }
}

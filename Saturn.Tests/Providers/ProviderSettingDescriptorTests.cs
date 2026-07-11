using System;
using FluentAssertions;
using Saturn.Providers;
using Xunit;

namespace Saturn.Tests.Providers
{
    public class ProviderSettingDescriptorTests
    {
        [Fact]
        public void Resolve_ExplicitSettingBeatsEnvironmentAndDefault()
        {
            var envVar = "SATURN_TEST_" + Guid.NewGuid().ToString("N");
            Environment.SetEnvironmentVariable(envVar, "from-env");
            try
            {
                var descriptor = new ProviderSettingDescriptor
                {
                    Key = "value",
                    Label = "Value",
                    DefaultValue = "from-default",
                    EnvironmentVariable = envVar
                };

                var settings = new ProviderSettings();
                settings.Set("value", "from-config");

                descriptor.Resolve(settings).Should().Be("from-config");
            }
            finally
            {
                Environment.SetEnvironmentVariable(envVar, null);
            }
        }

        [Fact]
        public void Resolve_EnvironmentBeatsDefault()
        {
            var envVar = "SATURN_TEST_" + Guid.NewGuid().ToString("N");
            Environment.SetEnvironmentVariable(envVar, "from-env");
            try
            {
                var descriptor = new ProviderSettingDescriptor
                {
                    Key = "value",
                    Label = "Value",
                    DefaultValue = "from-default",
                    EnvironmentVariable = envVar
                };

                descriptor.Resolve(new ProviderSettings()).Should().Be("from-env");
                descriptor.Resolve(null).Should().Be("from-env");
            }
            finally
            {
                Environment.SetEnvironmentVariable(envVar, null);
            }
        }

        [Fact]
        public void Resolve_FallsBackToDefault()
        {
            var descriptor = new ProviderSettingDescriptor
            {
                Key = "value",
                Label = "Value",
                DefaultValue = "from-default",
                EnvironmentVariable = "SATURN_TEST_UNSET_" + Guid.NewGuid().ToString("N")
            };

            descriptor.Resolve(new ProviderSettings()).Should().Be("from-default");
        }

        [Fact]
        public void Set_EmptyValue_RemovesKey()
        {
            var settings = new ProviderSettings();
            settings.Set("key", "value");
            settings.Set("key", "  ");

            settings.Get("key").Should().BeNull();
            settings.Values.Should().BeEmpty();
        }
    }
}

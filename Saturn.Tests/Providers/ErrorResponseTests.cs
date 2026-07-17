using FluentAssertions;
using Saturn.OpenRouter.Models.Api;
using Saturn.OpenRouter.Models.Api.Chat;
using Saturn.OpenRouter.Serialization;
using Xunit;

namespace Saturn.Tests.Providers
{
    public class ErrorResponseTests
    {
        [Fact]
        public void Deserialize_WithStringCode_SetsCodeNull()
        {
            const string json = """{"error":{"message":"boom","code":"model_not_loaded"}}""";

            var result = Json.Deserialize<ErrorResponse>(json, Json.CreateDefaultOptions());

            result.Should().NotBeNull();
            result!.Error.Should().NotBeNull();
            result.Error!.Code.Should().BeNull();
            result.Error.Message.Should().Be("boom");
        }

        [Fact]
        public void Deserialize_WithNumericCode_SetsCode()
        {
            const string json = """{"error":{"message":"x","code":429}}""";

            var result = Json.Deserialize<ErrorResponse>(json, Json.CreateDefaultOptions());

            result.Should().NotBeNull();
            result!.Error.Should().NotBeNull();
            result.Error!.Code.Should().Be(429);
            result.Error.Message.Should().Be("x");
        }

        [Fact]
        public void Deserialize_StreamingChoiceError_WithStringCode_SetsCodeNull()
        {
            const string json = """{"id":"1","choices":[{"delta":{},"error":{"message":"model not loaded","code":"model_not_loaded"}}]}""";

            var result = Json.Deserialize<ChatCompletionChunk>(json, Json.CreateDefaultOptions());

            result.Should().NotBeNull();
            result!.Choices.Should().NotBeNullOrEmpty();
            result.Choices![0].Error.Should().NotBeNull();
            result.Choices[0].Error!.Code.Should().BeNull();
            result.Choices[0].Error!.Message.Should().Be("model not loaded");
        }
    }
}

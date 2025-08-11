using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Saturn.OpenRouter.Models.Api.Chat
{
    /// <summary>
    /// Tool choice: "none" | "auto" | {"type":"function","function":{"name":...}}.
    /// Uses a custom converter to support the union.
    /// </summary>
    [JsonConverter(typeof(ToolChoice.Converter))]
    public sealed class ToolChoice
    {
        /// <summary>Indicates which kind of tool choice is represented.</summary>
        public ToolChoiceKind Kind { get; private set; }

        /// <summary>When Kind==Function, the function name to force.</summary>
        public string? FunctionName { get; private set; }

        private ToolChoice(ToolChoiceKind kind, string? functionName = null)
        {
            Kind = kind;
            FunctionName = functionName;
        }

        /// <summary>Create a "none" tool choice.</summary>
        public static ToolChoice None() => new ToolChoice(ToolChoiceKind.None);

        /// <summary>Create an "auto" tool choice.</summary>
        public static ToolChoice Auto() => new ToolChoice(ToolChoiceKind.Auto);

        /// <summary>Create a function tool choice with the given name.</summary>
        public static ToolChoice Function(string name) => new ToolChoice(ToolChoiceKind.Function, name);

        /// <summary>Discriminator for tool choice kind.</summary>
        public enum ToolChoiceKind
        {
            None,
            Auto,
            Function
        }

        internal sealed class Converter : JsonConverter<ToolChoice>
        {
            public override ToolChoice? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                if (reader.TokenType == JsonTokenType.String)
                {
                    var str = reader.GetString();
                    return str switch
                    {
                        "none" => ToolChoice.None(),
                        "auto" => ToolChoice.Auto(),
                        _ => throw new JsonException($"Unsupported tool_choice string: {str}")
                    };
                }

                if (reader.TokenType == JsonTokenType.StartObject)
                {
                    using var doc = JsonDocument.ParseValue(ref reader);
                    var root = doc.RootElement;

                    var typeProp = root.TryGetProperty("type", out var t) ? t.GetString() : null;
                    if (!string.Equals(typeProp, "function", StringComparison.OrdinalIgnoreCase))
                        throw new JsonException("tool_choice object must have type='function'.");

                    if (!root.TryGetProperty("function", out var funcElem))
                        throw new JsonException("tool_choice object missing 'function' property.");

                    var name = funcElem.TryGetProperty("name", out var nameElem) ? nameElem.GetString() : null;
                    if (string.IsNullOrWhiteSpace(name))
                        throw new JsonException("tool_choice.function.name is required.");

                    return ToolChoice.Function(name!);
                }

                throw new JsonException("Invalid tool_choice node.");
            }

            public override void Write(Utf8JsonWriter writer, ToolChoice value, JsonSerializerOptions options)
            {
                switch (value.Kind)
                {
                    case ToolChoiceKind.None:
                        writer.WriteStringValue("none");
                        break;
                    case ToolChoiceKind.Auto:
                        writer.WriteStringValue("auto");
                        break;
                    case ToolChoiceKind.Function:
                        writer.WriteStartObject();
                        writer.WriteString("type", "function");
                        writer.WritePropertyName("function");
                        writer.WriteStartObject();
                        writer.WriteString("name", value.FunctionName);
                        writer.WriteEndObject();
                        writer.WriteEndObject();
                        break;
                    default:
                        throw new JsonException("Unknown tool_choice kind.");
                }
            }
        }
    }
}

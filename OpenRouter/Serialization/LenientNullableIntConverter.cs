using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Saturn.OpenRouter.Serialization
{
    /// <summary>
    /// Some OpenAI-compatible servers (e.g. LM Studio) emit error codes as a
    /// string (e.g. "model_not_loaded") instead of a number. This converter
    /// tolerates that by treating any non-numeric string (or null) as the
    /// absence of a code, while still parsing numeric strings and JSON numbers.
    /// </summary>
    public sealed class LenientNullableIntConverter : JsonConverter<int?>
    {
        public override int? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.Null:
                    return null;
                case JsonTokenType.Number:
                    return reader.TryGetInt32(out var number) ? number : null;
                case JsonTokenType.String:
                    var text = reader.GetString();
                    return int.TryParse(text, out var parsed) ? parsed : null;
                default:
                    reader.Skip();
                    return null;
            }
        }

        public override void Write(Utf8JsonWriter writer, int? value, JsonSerializerOptions options)
        {
            if (value.HasValue)
            {
                writer.WriteNumberValue(value.Value);
            }
            else
            {
                writer.WriteNullValue();
            }
        }
    }
}

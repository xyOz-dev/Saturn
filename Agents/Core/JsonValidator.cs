using System;
using System.Text;
using System.Text.Json;

namespace Saturn.Agents.Core
{
    public static class JsonValidator
    {
        /// <summary>
        /// Validates if a JSON string is complete and well-formed
        /// </summary>
        public static bool IsCompleteJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return false;

            try
            {
                if (!HasBalancedBraces(json))
                    return false;

                using var doc = JsonDocument.Parse(json);
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        /// <summary>
        /// Checks if JSON has balanced braces and brackets
        /// </summary>
        private static bool HasBalancedBraces(string json)
        {
            int braceCount = 0;
            int bracketCount = 0;
            bool inString = false;
            bool escaped = false;

            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];

                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (c == '\\' && inString)
                {
                    escaped = true;
                    continue;
                }

                if (c == '"' && !escaped)
                {
                    inString = !inString;
                    continue;
                }

                if (!inString)
                {
                    switch (c)
                    {
                        case '{':
                            braceCount++;
                            break;
                        case '}':
                            braceCount--;
                            if (braceCount < 0) return false;
                            break;
                        case '[':
                            bracketCount++;
                            break;
                        case ']':
                            bracketCount--;
                            if (bracketCount < 0) return false;
                            break;
                    }
                }
            }

            return braceCount == 0 && bracketCount == 0 && !inString;
        }

        /// <summary>
        /// Attempts to safely parse JSON, returning null if invalid
        /// </summary>
        public static T? TryParseJson<T>(string json) where T : class
        {
            if (!IsCompleteJson(json))
                return null;

            try
            {
                return JsonSerializer.Deserialize<T>(json);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Validates and repairs common JSON issues in streamed content
        /// </summary>
        public static string RepairStreamedJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return "{}";

            var trimmed = json.Trim();
            
            if (IsCompleteJson(trimmed))
                return trimmed;

            var repaired = new StringBuilder(trimmed);

            int openBraces = 0;
            int openBrackets = 0;
            bool inString = false;
            bool escaped = false;

            for (int i = 0; i < trimmed.Length; i++)
            {
                char c = trimmed[i];

                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (c == '\\' && inString)
                {
                    escaped = true;
                    continue;
                }

                if (c == '"' && !escaped)
                {
                    inString = !inString;
                    continue;
                }

                if (!inString)
                {
                    switch (c)
                    {
                        case '{': openBraces++; break;
                        case '}': openBraces--; break;
                        case '[': openBrackets++; break;
                        case ']': openBrackets--; break;
                    }
                }
            }

            while (openBrackets > 0)
            {
                repaired.Append(']');
                openBrackets--;
            }

            while (openBraces > 0)
            {
                repaired.Append('}');
                openBraces--;
            }

            var repairedStr = repaired.ToString();
            return IsCompleteJson(repairedStr) ? repairedStr : "{}";
        }
    }

    /// <summary>
    /// Accumulates JSON fragments from streaming and validates completeness
    /// </summary>
    public class JsonStreamAccumulator
    {
        private readonly StringBuilder _buffer = new StringBuilder();
        private readonly int _maxBufferSize;

        public JsonStreamAccumulator(int maxBufferSize = 1048576) // 1MB default
        {
            _maxBufferSize = maxBufferSize;
        }

        public string CurrentBuffer => _buffer.ToString();
        public bool IsComplete => JsonValidator.IsCompleteJson(CurrentBuffer);
        public int Length => _buffer.Length;

        public void Append(string fragment)
        {
            if (_buffer.Length + fragment.Length > _maxBufferSize)
            {
                throw new InvalidOperationException($"JSON buffer exceeded maximum size of {_maxBufferSize} bytes");
            }

            _buffer.Append(fragment);
        }

        public void Clear()
        {
            _buffer.Clear();
        }

        public T? TryGetComplete<T>() where T : class
        {
            if (!IsComplete)
                return null;

            return JsonValidator.TryParseJson<T>(CurrentBuffer);
        }

        public string GetCompleteOrRepaired()
        {
            var current = CurrentBuffer;
            if (JsonValidator.IsCompleteJson(current))
                return current;

            return JsonValidator.RepairStreamedJson(current);
        }
    }
}
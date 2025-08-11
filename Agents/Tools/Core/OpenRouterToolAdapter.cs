using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Saturn.OpenRouter.Models.Api.Chat;
using Saturn.Tools.Core;

namespace Saturn.Agents.Tools.Core
{
    /// <summary>
    /// Adapter that maps Saturn.Tools.Core.ITool instances to Saturn.OpenRouter tool definitions.
    /// Behavior:
    /// - Name/Description are taken from ITool metadata.
    /// - Parameters JSON Schema:
    ///     - If the tool exposes a schema via a GetParametersSchema() method or a ParametersSchema property,
    ///       it is serialized to a JsonElement and used as ToolFunction.parameters.
    ///     - Otherwise, falls back to a generic object schema: { "type": "object", "properties": {}, "required": [] }.
    /// </summary>
    public static class OpenRouterToolAdapter
    {
        /// <summary>
        /// Convert a collection of ITool into a list of OpenRouter ToolDefinition objects.
        /// </summary>
        public static List<ToolDefinition> ToOpenRouterTools(IEnumerable<ITool> tools)
        {
            if (tools == null) return new List<ToolDefinition>();
            return tools.Select(ToOpenRouterTool).ToList();
        }

        /// <summary>
        /// Convert a single ITool into an OpenRouter ToolDefinition object.
        /// </summary>
        public static ToolDefinition ToOpenRouterTool(ITool tool)
        {
            if (tool == null) throw new ArgumentNullException(nameof(tool));

            var schemaOpt = TryGetSchemaFromTool(tool);
            var parametersSchema = schemaOpt ?? CreateGenericObjectSchema();

            return new ToolDefinition
            {
                Type = "function",
                Function = new ToolFunction
                {
                    Name = tool.Name,
                    Description = tool.Description,
                    Parameters = parametersSchema
                }
            };
        }

        private static JsonElement? TryGetSchemaFromTool(ITool tool)
        {
            // Prefer explicit schema if provided by the tool:
            // 1) Method: GetParametersSchema()
            var method = tool.GetType().GetMethod("GetParametersSchema", Type.EmptyTypes);
            if (method != null)
            {
                var schemaObj = method.Invoke(tool, null);
                if (schemaObj != null)
                {
                    return SerializeToElement(schemaObj);
                }
            }

            // 2) Property: ParametersSchema
            var prop = tool.GetType().GetProperty("ParametersSchema");
            if (prop != null)
            {
                var schemaObj = prop.GetValue(tool);
                if (schemaObj != null)
                {
                    return SerializeToElement(schemaObj);
                }
            }

            // 3) Heuristic: If GetParameters() returns a schema-like object, use it. Otherwise we fallback later.
            try
            {
                var parameters = tool.GetParameters();
                if (parameters != null)
                {
                    var looksLikeSchema =
                        parameters.ContainsKey("type") ||
                        parameters.ContainsKey("properties") ||
                        parameters.ContainsKey("$schema");

                    if (looksLikeSchema)
                    {
                        return SerializeToElement(parameters);
                    }
                }
            }
            catch
            {
                // Ignore and fallback to generic schema
            }

            // No explicit/heuristic schema found
            return null;
        }

        private static JsonElement CreateGenericObjectSchema()
        {
            var fallback = new
            {
                type = "object",
                properties = new Dictionary<string, object>(),
                required = new List<string>()
            };
            return JsonSerializer.SerializeToElement(fallback);
        }

        private static JsonElement SerializeToElement(object obj)
        {
            if (obj is JsonElement je) return je;
            return JsonSerializer.SerializeToElement(obj);
        }
    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Saturn.OpenRouter.Models.Api.Chat;
using Saturn.Tools.Core;

namespace Saturn.Agents.Tools.Core
{
    public static class OpenRouterToolAdapter
    {
        public static List<ToolDefinition> ToOpenRouterTools(IEnumerable<ITool> tools)
        {
            if (tools == null) return new List<ToolDefinition>();
            return tools.Select(ToOpenRouterTool).ToList();
        }

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
            var method = tool.GetType().GetMethod("GetParametersSchema", Type.EmptyTypes);
            if (method != null)
            {
                var schemaObj = method.Invoke(tool, null);
                if (schemaObj != null)
                {
                    return SerializeToElement(schemaObj);
                }
            }

            var prop = tool.GetType().GetProperty("ParametersSchema");
            if (prop != null)
            {
                var schemaObj = prop.GetValue(tool);
                if (schemaObj != null)
                {
                    return SerializeToElement(schemaObj);
                }
            }

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
            }

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
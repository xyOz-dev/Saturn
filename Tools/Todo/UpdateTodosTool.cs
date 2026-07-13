using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Saturn.Tools.Core;

namespace Saturn.Tools.Todo
{
    public class UpdateTodosTool : ToolBase
    {
        private const int MaxItems = 50;

        public override string Name => "update_todos";

        public override string Description => @"Use this tool to track your plan for the current task as a todo checklist. It is scoped to this chat session, persists across restarts with it, and is not shown to other agents.

When to use:
- At the start of any multi-step task: write out the full step list
- After finishing a step: mark it completed and move to the next
- When the plan changes: rewrite the list to match reality

How to use:
- Send the COMPLETE list every call; it replaces the previous list entirely
- Each item has 'content' (imperative description) and 'status' (pending, in_progress, or completed)
- Keep at most one item in_progress at a time
- Mark items completed as soon as they are done, not in batches
- An empty array clears the list

Important rules:
- This list is your private scratchpad for plan tracking. For durable, user-visible task boards use the task tools (create_task, list_tasks, ...) instead.";

        protected override Dictionary<string, object> GetParameterProperties()
        {
            return new Dictionary<string, object>
            {
                { "todos", new Dictionary<string, object>
                    {
                        { "type", "array" },
                        { "description", "The complete todo list. Replaces the previous list. An empty array clears the list" },
                        { "items", new Dictionary<string, object>
                            {
                                { "type", "object" },
                                { "properties", new Dictionary<string, object>
                                    {
                                        { "content", new Dictionary<string, object>
                                            {
                                                { "type", "string" },
                                                { "description", "Imperative description of the step, e.g. 'Add tests for the parser'" }
                                            }
                                        },
                                        { "status", new Dictionary<string, object>
                                            {
                                                { "type", "string" },
                                                { "enum", new[] { "pending", "in_progress", "completed" } },
                                                { "description", "Current state of this step" }
                                            }
                                        }
                                    }
                                },
                                { "required", new[] { "content", "status" } }
                            }
                        }
                    }
                }
            };
        }

        protected override string[] GetRequiredParameters()
        {
            return new[] { "todos" };
        }

        public override string GetDisplaySummary(Dictionary<string, object> parameters)
        {
            if (parameters.TryGetValue("todos", out var value) && value is List<object> list)
            {
                return list.Count == 0
                    ? "Clearing todo list"
                    : $"Updating todo list ({list.Count} item{(list.Count == 1 ? "" : "s")})";
            }

            return "Updating todo list";
        }

        public override async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters)
        {
            if (!parameters.TryGetValue("todos", out var rawTodos) || rawTodos is not List<object> rawList)
            {
                return CreateErrorResult("todos must be an array of {content, status} objects");
            }

            if (rawList.Count > MaxItems)
            {
                return CreateErrorResult($"Too many todos ({rawList.Count}); keep the list to at most {MaxItems} items");
            }

            var items = new List<TodoItem>(rawList.Count);
            var inProgressCount = 0;

            for (var i = 0; i < rawList.Count; i++)
            {
                if (rawList[i] is not Dictionary<string, object> item)
                {
                    return CreateErrorResult($"todos[{i}] must be an object with 'content' and 'status'");
                }

                item.TryGetValue("content", out var contentValue);
                var content = contentValue as string;
                if (string.IsNullOrWhiteSpace(content))
                {
                    return CreateErrorResult($"todos[{i}].content must be a non-empty string");
                }

                item.TryGetValue("status", out var statusValue);
                if (!TodoStore.TryParseStatus(statusValue as string, out var status))
                {
                    return CreateErrorResult($"todos[{i}].status must be one of: pending, in_progress, completed");
                }

                if (status == TodoStatus.InProgress && ++inProgressCount > 1)
                {
                    return CreateErrorResult("Only one item may be in_progress at a time");
                }

                items.Add(new TodoItem(content.Trim(), status));
            }

            await TodoStore.SetAsync(TodoStore.CurrentKey(), items);

            if (items.Count == 0)
            {
                return CreateSuccessResult(new { count = 0, todos = Array.Empty<object>() }, "Todo list cleared.");
            }

            var completed = items.Count(t => t.Status == TodoStatus.Completed);
            var pending = items.Count(t => t.Status == TodoStatus.Pending);

            var output = new StringBuilder();
            output.AppendLine($"Todo list updated ({items.Count} item{(items.Count == 1 ? "" : "s")}: {completed} completed, {inProgressCount} in progress, {pending} pending):");
            foreach (var item in items)
            {
                var marker = item.Status switch
                {
                    TodoStatus.Completed => "[x]",
                    TodoStatus.InProgress => "[~]",
                    _ => "[ ]"
                };
                output.AppendLine($"{marker} {item.Content}");
            }

            var rawData = new
            {
                count = items.Count,
                completed,
                inProgress = inProgressCount,
                pending,
                todos = items.Select(t => new { content = t.Content, status = TodoStore.StatusToString(t.Status) }).ToList()
            };

            return CreateSuccessResult(rawData, output.ToString().TrimEnd());
        }
    }
}

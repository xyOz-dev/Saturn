using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Saturn.Data;
using Saturn.Tools.Core;

namespace Saturn.Tools.Todo
{
    public enum TodoStatus
    {
        Pending,
        InProgress,
        Completed
    }

    public sealed record TodoItem(string Content, TodoStatus Status);

    public static class TodoStore
    {
        public const string NoSessionKey = "(no-session)";

        private static readonly ConcurrentDictionary<string, IReadOnlyList<TodoItem>> _lists = new();

        // Own repository instance on the shared chats.db; per-agent repositories
        // already coexist on the same file (WAL + busy_timeout).
        private static readonly Lazy<ChatHistoryRepository?> _repository = new(() =>
        {
            try
            {
                return new ChatHistoryRepository();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to open todo persistence store: {ex.Message}");
                return null;
            }
        });

        public static string CurrentKey()
        {
            var sessionId = AgentContext.Current?.SessionId;
            return string.IsNullOrEmpty(sessionId) ? NoSessionKey : sessionId;
        }

        public static async Task<IReadOnlyList<TodoItem>> GetAsync(string key)
        {
            if (_lists.TryGetValue(key, out var cached))
            {
                return cached;
            }

            if (key != NoSessionKey && _repository.Value != null)
            {
                try
                {
                    var json = await _repository.Value.GetSessionTodosAsync(key);
                    if (!string.IsNullOrEmpty(json))
                    {
                        var items = Deserialize(json);
                        _lists[key] = items;
                        return items;
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Failed to load todos for session {key}: {ex.Message}");
                }
            }

            return Array.Empty<TodoItem>();
        }

        public static async Task SetAsync(string key, IReadOnlyList<TodoItem> items)
        {
            if (items.Count == 0)
            {
                _lists.TryRemove(key, out _);
            }
            else
            {
                _lists[key] = items;
            }

            if (key == NoSessionKey || _repository.Value == null)
            {
                return;
            }

            try
            {
                await _repository.Value.SaveSessionTodosAsync(key, items.Count == 0 ? null : Serialize(items));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to persist todos for session {key}: {ex.Message}");
            }
        }

        public static string StatusToString(TodoStatus status) => status switch
        {
            TodoStatus.InProgress => "in_progress",
            TodoStatus.Completed => "completed",
            _ => "pending"
        };

        public static bool TryParseStatus(string? value, out TodoStatus status)
        {
            switch (value)
            {
                case "pending":
                    status = TodoStatus.Pending;
                    return true;
                case "in_progress":
                    status = TodoStatus.InProgress;
                    return true;
                case "completed":
                    status = TodoStatus.Completed;
                    return true;
                default:
                    status = TodoStatus.Pending;
                    return false;
            }
        }

        private sealed record TodoItemDto(string content, string status);

        private static string Serialize(IReadOnlyList<TodoItem> items)
        {
            var dtos = items.Select(i => new TodoItemDto(i.Content, StatusToString(i.Status))).ToList();
            return JsonSerializer.Serialize(dtos);
        }

        private static IReadOnlyList<TodoItem> Deserialize(string json)
        {
            var dtos = JsonSerializer.Deserialize<List<TodoItemDto>>(json) ?? new List<TodoItemDto>();
            return dtos
                .Where(d => !string.IsNullOrEmpty(d.content))
                .Select(d => new TodoItem(d.content, TryParseStatus(d.status, out var s) ? s : TodoStatus.Pending))
                .ToList();
        }
    }
}

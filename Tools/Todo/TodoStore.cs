using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
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

        private const int MaxCacheEntries = 256;

        private static readonly ConcurrentDictionary<string, IReadOnlyList<TodoItem>> _lists = new();
        private static readonly ConcurrentDictionary<string, long> _lastAccess = new();
        private static long _accessCounter;

        // Own repository instance on the shared chats.db; per-agent repositories
        // already coexist on the same file (WAL + busy_timeout).
        private static Lazy<ChatHistoryRepository?> _repository = new(CreateDefaultRepository);

        private static ChatHistoryRepository? CreateDefaultRepository()
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
        }

        // Test seam: unit tests must not share the process-wide chats.db in the
        // current directory, so Saturn.Tests redirects persistence to an
        // isolated workspace before any repository access happens.
        internal static void OverrideRepositoryFactory(Func<ChatHistoryRepository?> factory)
        {
            var previous = _repository;
            _repository = new Lazy<ChatHistoryRepository?>(factory);
            if (previous.IsValueCreated)
            {
                previous.Value?.Dispose();
            }
        }

        public static string CurrentKey()
        {
            var context = AgentContext.Current;
            if (!string.IsNullOrEmpty(context?.SessionId))
            {
                return context!.SessionId!;
            }

            if (!string.IsNullOrEmpty(context?.AgentInstanceId))
            {
                return $"(agent:{context!.AgentInstanceId})";
            }

            return NoSessionKey;
        }

        // Parenthesized keys are agent-instance fallbacks with no session to
        // persist under; they live in memory only.
        private static bool IsPersistable(string key) => !key.StartsWith("(");

        public static async Task<IReadOnlyList<TodoItem>> GetAsync(string key)
        {
            if (_lists.TryGetValue(key, out var cached))
            {
                Touch(key);
                return cached;
            }

            if (IsPersistable(key) && _repository.Value != null)
            {
                try
                {
                    var json = await _repository.Value.GetSessionTodosAsync(key);
                    if (!string.IsNullOrEmpty(json))
                    {
                        var items = Deserialize(json);
                        _lists[key] = items;
                        Touch(key);
                        EvictIfOverCap(key);
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

        /// <summary>
        /// Updates the list for the given key. Returns false when the list should
        /// have been persisted but the write failed, so callers can warn that it
        /// may not survive a restart.
        /// </summary>
        public static async Task<bool> SetAsync(string key, IReadOnlyList<TodoItem> items)
        {
            if (items.Count == 0)
            {
                _lists.TryRemove(key, out _);
                _lastAccess.TryRemove(key, out _);
            }
            else
            {
                _lists[key] = items;
                Touch(key);
                EvictIfOverCap(key);
            }

            if (!IsPersistable(key))
            {
                return true;
            }

            if (_repository.Value == null)
            {
                return false;
            }

            try
            {
                await _repository.Value.SaveSessionTodosAsync(key, items.Count == 0 ? null : Serialize(items));
                return true;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to persist todos for session {key}: {ex.Message}");
                return false;
            }
        }

        private static void Touch(string key)
        {
            _lastAccess[key] = Interlocked.Increment(ref _accessCounter);
        }

        // Sessions are unbounded over a long-lived web process; persisted entries
        // reload from the DB on the next access, so evicting them only costs a read.
        // Non-persistable entries can be lost, which is acceptable for the rare
        // sessionless caller once the cache holds hundreds of lists.
        private static void EvictIfOverCap(string currentKey)
        {
            if (_lists.Count <= MaxCacheEntries)
            {
                return;
            }

            var evictable = _lastAccess
                .Where(kv => kv.Key != currentKey)
                .OrderBy(kv => kv.Value)
                .Select(kv => kv.Key)
                .Take(_lists.Count - MaxCacheEntries)
                .ToList();

            foreach (var key in evictable)
            {
                _lists.TryRemove(key, out _);
                _lastAccess.TryRemove(key, out _);
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

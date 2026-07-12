using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Saturn.Web
{
    public class TodoItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
        public string Title { get; set; } = "";
        public string? Notes { get; set; }
        public string Status { get; set; } = "pending";
        public string Priority { get; set; } = "normal";
        public int Order { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }
    }

    public class TodoStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
        private static readonly string[] ValidStatuses = { "pending", "in_progress", "done" };
        private static readonly string[] ValidPriorities = { "low", "normal", "high" };

        private readonly string _filePath;
        private readonly object _lock = new();
        private List<TodoItem> _items;

        public TodoStore(string? workspacePath = null)
        {
            var directory = Path.Combine(workspacePath ?? Directory.GetCurrentDirectory(), ".saturn");
            Directory.CreateDirectory(directory);
            _filePath = Path.Combine(directory, "todos.json");
            _items = Load();
        }

        public List<TodoItem> GetAll()
        {
            lock (_lock)
            {
                return _items.OrderBy(t => t.Order).ThenBy(t => t.CreatedAt).ToList();
            }
        }

        public TodoItem Add(string title, string? notes, string? priority)
        {
            lock (_lock)
            {
                var item = new TodoItem
                {
                    Title = title.Trim(),
                    Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
                    Priority = NormalizePriority(priority),
                    Order = _items.Count == 0 ? 0 : _items.Max(t => t.Order) + 1
                };
                _items.Add(item);
                Save();
                return item;
            }
        }

        public TodoItem? Update(string id, string? title, string? notes, string? status, string? priority, int? order)
        {
            lock (_lock)
            {
                var item = _items.FirstOrDefault(t => t.Id == id);
                if (item == null)
                {
                    return null;
                }

                if (!string.IsNullOrWhiteSpace(title))
                {
                    item.Title = title.Trim();
                }
                if (notes != null)
                {
                    item.Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
                }
                if (status != null && ValidStatuses.Contains(status))
                {
                    item.Status = status;
                    item.CompletedAt = status == "done" ? DateTime.UtcNow : null;
                }
                if (priority != null)
                {
                    item.Priority = NormalizePriority(priority);
                }
                if (order.HasValue)
                {
                    Reorder(item, order.Value);
                }

                Save();
                return item;
            }
        }

        public bool Delete(string id)
        {
            lock (_lock)
            {
                var removed = _items.RemoveAll(t => t.Id == id) > 0;
                if (removed)
                {
                    Save();
                }
                return removed;
            }
        }

        public int ClearCompleted()
        {
            lock (_lock)
            {
                var removed = _items.RemoveAll(t => t.Status == "done");
                if (removed > 0)
                {
                    Save();
                }
                return removed;
            }
        }

        private void Reorder(TodoItem item, int newOrder)
        {
            var ordered = _items.OrderBy(t => t.Order).ThenBy(t => t.CreatedAt).ToList();
            ordered.Remove(item);
            newOrder = Math.Clamp(newOrder, 0, ordered.Count);
            ordered.Insert(newOrder, item);
            for (var i = 0; i < ordered.Count; i++)
            {
                ordered[i].Order = i;
            }
        }

        private static string NormalizePriority(string? priority)
        {
            return priority != null && ValidPriorities.Contains(priority) ? priority : "normal";
        }

        private List<TodoItem> Load()
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    var json = File.ReadAllText(_filePath);
                    return JsonSerializer.Deserialize<List<TodoItem>>(json, JsonOptions) ?? new List<TodoItem>();
                }
            }
            catch (Exception)
            {
                // A corrupt todo file should not prevent startup; start fresh.
            }
            return new List<TodoItem>();
        }

        private void Save()
        {
            File.WriteAllText(_filePath, JsonSerializer.Serialize(_items, JsonOptions));
        }
    }
}

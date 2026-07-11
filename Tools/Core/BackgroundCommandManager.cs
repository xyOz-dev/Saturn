using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace Saturn.Tools.Core
{
    public enum BackgroundCommandStatus
    {
        Running,
        Exited,
        Killed
    }

    /// <summary>
    /// A command launched in the background. Output is buffered as it arrives and can be
    /// drained incrementally via <see cref="ReadNew"/>, mirroring how a terminal is polled.
    /// </summary>
    public class BackgroundCommand
    {
        private const int MaxBufferLength = 1048576; // 1MB retained per stream

        private readonly StringBuilder _stdout = new();
        private readonly StringBuilder _stderr = new();
        private int _stdoutCursor;
        private int _stderrCursor;
        private readonly object _lock = new();

        public string Id { get; init; } = "";
        public string Command { get; init; } = "";
        public string WorkingDirectory { get; init; } = "";
        public Process Process { get; init; } = null!;
        public DateTime StartedAt { get; init; }
        public BackgroundCommandStatus Status { get; set; } = BackgroundCommandStatus.Running;
        public int? ExitCode { get; set; }

        public void AppendStdout(string line)
        {
            lock (_lock) Append(_stdout, ref _stdoutCursor, line);
        }

        public void AppendStderr(string line)
        {
            lock (_lock) Append(_stderr, ref _stderrCursor, line);
        }

        /// <summary>
        /// Returns output produced since the previous call and advances the read cursors.
        /// </summary>
        public (string StdOut, string StdErr) ReadNew()
        {
            lock (_lock)
            {
                var outNew = _stdout.ToString(_stdoutCursor, _stdout.Length - _stdoutCursor);
                var errNew = _stderr.ToString(_stderrCursor, _stderr.Length - _stderrCursor);
                _stdoutCursor = _stdout.Length;
                _stderrCursor = _stderr.Length;
                return (outNew, errNew);
            }
        }

        private static void Append(StringBuilder buffer, ref int cursor, string line)
        {
            buffer.AppendLine(line);
            if (buffer.Length > MaxBufferLength)
            {
                // Drop from the front so the most recent output (and unread tail) is retained.
                var overflow = buffer.Length - MaxBufferLength;
                buffer.Remove(0, overflow);
                cursor = Math.Max(0, cursor - overflow);
            }
        }
    }

    /// <summary>
    /// Tracks commands started in the background so their output can be polled and they can be
    /// killed on demand. Mirrors the lifecycle role AgentManager plays for sub-agents.
    /// </summary>
    public class BackgroundCommandManager
    {
        private static readonly Lazy<BackgroundCommandManager> _instance = new(() => new BackgroundCommandManager());
        public static BackgroundCommandManager Instance => _instance.Value;

        private readonly ConcurrentDictionary<string, BackgroundCommand> _commands = new();
        private int _counter;

        private BackgroundCommandManager() { }

        public BackgroundCommand Register(string command, string workingDirectory, Process process)
        {
            var id = $"cmd_{Interlocked.Increment(ref _counter)}";
            var bg = new BackgroundCommand
            {
                Id = id,
                Command = command,
                WorkingDirectory = workingDirectory,
                Process = process,
                StartedAt = DateTime.UtcNow
            };
            _commands[id] = bg;
            return bg;
        }

        public BackgroundCommand? Get(string id)
        {
            return id != null && _commands.TryGetValue(id, out var cmd) ? cmd : null;
        }

        public IEnumerable<BackgroundCommand> GetAll() => _commands.Values;

        public void Remove(string id) => _commands.TryRemove(id, out _);
    }
}

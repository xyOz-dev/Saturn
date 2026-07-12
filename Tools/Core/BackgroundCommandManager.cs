using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
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
    public class BackgroundCommand : IDisposable
    {
        private const int MaxBufferLength = 1048576; // 1MB retained per stream

        private readonly StringBuilder _stdout = new();
        private readonly StringBuilder _stderr = new();
        private int _stdoutCursor;
        private int _stderrCursor;
        private readonly object _lock = new();
        private BackgroundCommandStatus _status = BackgroundCommandStatus.Running;

        public string Id { get; init; } = "";
        public string Command { get; init; } = "";
        public string WorkingDirectory { get; init; } = "";
        public Process Process { get; init; } = null!;
        public DateTime StartedAt { get; init; }
        public int? ExitCode { get; private set; }

        public BackgroundCommandStatus Status
        {
            get { lock (_lock) { return _status; } }
        }

        public bool IsTerminal
        {
            get { lock (_lock) { return _status != BackgroundCommandStatus.Running; } }
        }

        /// <summary>Records a natural exit. No-op if the command was already killed or exited.</summary>
        public void MarkExited(int? exitCode)
        {
            lock (_lock)
            {
                if (_status != BackgroundCommandStatus.Running)
                    return;
                _status = BackgroundCommandStatus.Exited;
                ExitCode = exitCode;
            }
        }

        /// <summary>Records that the command was killed. Returns false if it had already finished.</summary>
        public bool MarkKilled()
        {
            lock (_lock)
            {
                if (_status != BackgroundCommandStatus.Running)
                    return false;
                _status = BackgroundCommandStatus.Killed;
                return true;
            }
        }

        /// <summary>
        /// Undoes a <see cref="MarkKilled"/> claim when termination could not be carried out,
        /// reconciling with the process's actual state so the command is not stuck as 'killed'.
        /// </summary>
        public void RevertKill()
        {
            lock (_lock)
            {
                if (_status != BackgroundCommandStatus.Killed)
                    return;

                bool exited;
                try { exited = Process != null && Process.HasExited; } catch { exited = false; }

                if (exited)
                {
                    _status = BackgroundCommandStatus.Exited;
                    try { ExitCode = Process!.ExitCode; } catch { }
                }
                else
                {
                    _status = BackgroundCommandStatus.Running;
                }
            }
        }

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

        public void Dispose()
        {
            try { Process?.Dispose(); } catch { }
        }
    }

    /// <summary>
    /// Tracks commands started in the background so their output can be polled and they can be
    /// killed on demand. Mirrors the lifecycle role AgentManager plays for sub-agents.
    /// </summary>
    public class BackgroundCommandManager
    {
        // Retain a bounded number of commands so finished processes don't leak handles or memory
        // over a long session. Only terminal (exited/killed) commands are ever evicted.
        private const int MaxRetainedCommands = 50;

        private static readonly Lazy<BackgroundCommandManager> _instance = new(() => new BackgroundCommandManager());
        public static BackgroundCommandManager Instance => _instance.Value;

        private readonly ConcurrentDictionary<string, BackgroundCommand> _commands = new();
        private int _counter;

        private BackgroundCommandManager()
        {
            // Ensure background children don't outlive the host process.
            AppDomain.CurrentDomain.ProcessExit += (_, _) => Shutdown();
        }

        /// <summary>Terminates and disposes every tracked command, including running ones.</summary>
        public void Shutdown()
        {
            foreach (var cmd in _commands.Values)
            {
                try
                {
                    if (cmd.MarkKilled())
                        cmd.Process.Kill(entireProcessTree: true);
                }
                catch { }
            }

            foreach (var id in _commands.Keys.ToList())
            {
                Remove(id);
            }
        }

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
            EvictTerminalOverflow();
            return bg;
        }

        public BackgroundCommand? Get(string id)
        {
            return id != null && _commands.TryGetValue(id, out var cmd) ? cmd : null;
        }

        public IEnumerable<BackgroundCommand> GetAll() => _commands.Values;

        public void Remove(string id)
        {
            if (id != null && _commands.TryRemove(id, out var cmd))
            {
                cmd.Dispose();
            }
        }

        // Drop the oldest finished commands once we exceed the retention cap. Running commands are
        // never evicted so their output stays pollable until they finish.
        private void EvictTerminalOverflow()
        {
            var overflow = _commands.Count - MaxRetainedCommands;
            if (overflow <= 0)
                return;

            var stale = _commands.Values
                .Where(c => c.IsTerminal)
                .OrderBy(c => c.StartedAt)
                .Take(overflow)
                .ToList();

            foreach (var cmd in stale)
            {
                Remove(cmd.Id);
            }
        }
    }
}

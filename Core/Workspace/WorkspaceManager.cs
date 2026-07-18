using System;
using System.IO;

namespace Saturn.Core.Workspace
{
    public record WorkspaceSwitchResult(bool Success, string? NormalizedPath, string? Error);

    /// <summary>
    /// Owns the current workspace root. Until <see cref="Initialize"/> is called the
    /// workspace falls back to the live process working directory, so code (and tests)
    /// that rely on Directory.SetCurrentDirectory keep working unchanged.
    /// </summary>
    public static class WorkspaceManager
    {
        private static readonly object Sync = new();
        private static string? _current;

        public static string CurrentWorkspace
        {
            get
            {
                lock (Sync)
                {
                    return _current ?? Environment.CurrentDirectory;
                }
            }
        }

        public static string WorkspaceName
        {
            get
            {
                var path = CurrentWorkspace;
                var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
                return string.IsNullOrEmpty(name) ? path : name;
            }
        }

        public static event Action<string, string>? WorkspaceChanged;

        /// <summary>
        /// Path equality semantics for the local filesystem: case-insensitive on
        /// Windows and macOS, case-sensitive elsewhere.
        /// </summary>
        public static StringComparison PathComparison =>
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

        public static void Initialize(string? path)
        {
            lock (Sync)
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    _current = Normalize(Environment.CurrentDirectory);
                    return;
                }

                var normalized = Normalize(path);
                if (!Directory.Exists(normalized))
                {
                    throw new DirectoryNotFoundException($"Workspace directory does not exist: {normalized}");
                }

                _current = normalized;
            }
        }

        public static WorkspaceSwitchResult TrySwitch(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return new WorkspaceSwitchResult(false, null, "Workspace path is empty.");
            }

            string oldPath;
            string normalized;
            lock (Sync)
            {
                try
                {
                    normalized = Normalize(path);
                }
                catch (Exception ex)
                {
                    return new WorkspaceSwitchResult(false, null, $"Invalid workspace path: {ex.Message}");
                }

                if (File.Exists(normalized))
                {
                    return new WorkspaceSwitchResult(false, null, "Workspace path points to a file, not a directory.");
                }

                if (!Directory.Exists(normalized))
                {
                    return new WorkspaceSwitchResult(false, null, $"Directory does not exist: {normalized}");
                }

                oldPath = _current ?? Environment.CurrentDirectory;
                if (string.Equals(oldPath, normalized, PathComparison))
                {
                    return new WorkspaceSwitchResult(true, normalized, null);
                }

                _current = normalized;
            }

            WorkspaceChanged?.Invoke(oldPath, normalized);
            return new WorkspaceSwitchResult(true, normalized, null);
        }

        private static string Normalize(string path)
        {
            // Relative paths resolve against the current workspace (which is the
            // launch CWD before Initialize), not the raw process CWD.
            var basePath = _current ?? Environment.CurrentDirectory;
            var full = Path.GetFullPath(path, basePath);

            var trimmed = Path.TrimEndingDirectorySeparator(full);
            // TrimEndingDirectorySeparator("C:\") returns "C:", which is a
            // drive-relative path; keep the root form for drive roots.
            return Path.GetPathRoot(full) == full ? full : trimmed;
        }

        internal static void ResetForTests()
        {
            lock (Sync)
            {
                _current = null;
            }
        }
    }
}

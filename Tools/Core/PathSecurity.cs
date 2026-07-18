using System;
using System.IO;
using System.Security;
using Saturn.Core.Workspace;

namespace Saturn.Tools.Core
{
    public static class PathSecurity
    {
        public static void ValidateInsideWorkingDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Path cannot be null or empty");
            }

            var workingDirectory = Path.GetFullPath(WorkspaceManager.CurrentWorkspace);
            var fullPath = Path.GetFullPath(path, workingDirectory);

            // Lexical check first: cheap, and catches plain traversal even when
            // the filesystem cannot be probed.
            EnsureRelativeStaysInside(workingDirectory, fullPath, path);

            // Then compare the real locations so a symlink, junction, or other
            // reparse point inside the workspace cannot smuggle the path outside.
            var resolvedPath = ResolveRealPath(fullPath);
            var resolvedWorkingDirectory = ResolveRealPath(workingDirectory);
            EnsureRelativeStaysInside(resolvedWorkingDirectory, resolvedPath, path);
        }

        private static void EnsureRelativeStaysInside(string baseDirectory, string fullPath, string originalPath)
        {
            var relative = Path.GetRelativePath(baseDirectory, fullPath);
            if (Path.IsPathRooted(relative) ||
                relative == ".." ||
                relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
            {
                throw new SecurityException($"Access denied: Path '{originalPath}' is outside the working directory");
            }
        }

        private static string ResolveRealPath(string fullPath)
        {
            var root = Path.GetPathRoot(fullPath);
            if (string.IsNullOrEmpty(root))
            {
                return fullPath;
            }

            var segments = fullPath.Substring(root.Length).Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries);

            var current = root;
            for (int i = 0; i < segments.Length; i++)
            {
                current = Path.Combine(current, segments[i]);

                FileSystemInfo info;
                if (Directory.Exists(current))
                {
                    info = new DirectoryInfo(current);
                }
                else if (File.Exists(current))
                {
                    info = new FileInfo(current);
                }
                else
                {
                    // The remainder does not exist yet (e.g. a file about to be
                    // created); nothing further can be a link.
                    for (int j = i + 1; j < segments.Length; j++)
                    {
                        current = Path.Combine(current, segments[j]);
                    }
                    break;
                }

                try
                {
                    var target = info.ResolveLinkTarget(returnFinalTarget: true);
                    if (target != null)
                    {
                        current = target.FullName;
                    }
                }
                catch (IOException)
                {
                    // Broken or cyclic link; keep the lexical path and let the
                    // caller's IO fail naturally.
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            return Path.GetFullPath(current);
        }
    }
}

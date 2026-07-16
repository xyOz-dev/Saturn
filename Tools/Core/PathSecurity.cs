using System;
using System.IO;
using System.Security;

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

            var fullPath = Path.GetFullPath(path);
            var workingDirectory = Path.GetFullPath(Directory.GetCurrentDirectory());

            var relative = Path.GetRelativePath(workingDirectory, fullPath);
            if (Path.IsPathRooted(relative) ||
                relative == ".." ||
                relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
            {
                throw new SecurityException($"Access denied: Path '{path}' is outside the working directory");
            }
        }
    }
}

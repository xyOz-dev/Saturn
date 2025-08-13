using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Saturn.Tests.TestHelpers
{
    /// <summary>
    /// Helper class for file-based tests that manages temporary files and directories
    /// </summary>
    public class FileTestHelper : IDisposable
    {
        private readonly string _testDirectory;
        private readonly List<string> _createdFiles;
        private readonly List<string> _createdDirectories;

        public string TestDirectory => _testDirectory;

        public FileTestHelper(string testName = null)
        {
            var dirName = testName ?? $"SaturnTest_{Guid.NewGuid():N}";
            _testDirectory = Path.Combine(Path.GetTempPath(), dirName);
            Directory.CreateDirectory(_testDirectory);
            
            _createdFiles = new List<string>();
            _createdDirectories = new List<string> { _testDirectory };
        }

        /// <summary>
        /// Creates a test file with the specified content
        /// </summary>
        public string CreateFile(string relativePath, string content, Encoding encoding = null)
        {
            var fullPath = Path.Combine(_testDirectory, relativePath);
            var directory = Path.GetDirectoryName(fullPath);
            
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                _createdDirectories.Add(directory);
            }

            File.WriteAllText(fullPath, content, encoding ?? Encoding.UTF8);
            _createdFiles.Add(fullPath);
            
            return fullPath;
        }

        /// <summary>
        /// Creates a test file with binary content
        /// </summary>
        public string CreateBinaryFile(string relativePath, byte[] content)
        {
            var fullPath = Path.Combine(_testDirectory, relativePath);
            var directory = Path.GetDirectoryName(fullPath);
            
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                _createdDirectories.Add(directory);
            }

            File.WriteAllBytes(fullPath, content);
            _createdFiles.Add(fullPath);
            
            return fullPath;
        }

        /// <summary>
        /// Creates an empty directory
        /// </summary>
        public string CreateDirectory(string relativePath)
        {
            var fullPath = Path.Combine(_testDirectory, relativePath);
            Directory.CreateDirectory(fullPath);
            _createdDirectories.Add(fullPath);
            return fullPath;
        }

        /// <summary>
        /// Gets the full path for a file in the test directory
        /// </summary>
        public string GetPath(string relativePath)
        {
            return Path.Combine(_testDirectory, relativePath);
        }

        /// <summary>
        /// Reads the content of a test file
        /// </summary>
        public string ReadFile(string relativePath)
        {
            var fullPath = Path.Combine(_testDirectory, relativePath);
            return File.ReadAllText(fullPath);
        }

        /// <summary>
        /// Checks if a file exists in the test directory
        /// </summary>
        public bool FileExists(string relativePath)
        {
            var fullPath = Path.Combine(_testDirectory, relativePath);
            return File.Exists(fullPath);
        }

        /// <summary>
        /// Creates a large file for testing
        /// </summary>
        public string CreateLargeFile(string relativePath, int numberOfLines)
        {
            var content = new StringBuilder();
            for (int i = 0; i < numberOfLines; i++)
            {
                content.AppendLine($"Line {i + 1}: This is test content for line number {i + 1} with some padding to make it longer");
            }
            return CreateFile(relativePath, content.ToString());
        }

        public void Dispose()
        {
            // Delete files first
            foreach (var file in _createdFiles)
            {
                try
                {
                    if (File.Exists(file))
                    {
                        File.SetAttributes(file, FileAttributes.Normal);
                        File.Delete(file);
                    }
                }
                catch
                {
                    // Ignore errors during cleanup
                }
            }

            // Delete directories in reverse order (deepest first)
            _createdDirectories.Reverse();
            foreach (var directory in _createdDirectories)
            {
                try
                {
                    if (Directory.Exists(directory))
                    {
                        Directory.Delete(directory, true);
                    }
                }
                catch
                {
                    // Ignore errors during cleanup
                }
            }
        }
    }
}
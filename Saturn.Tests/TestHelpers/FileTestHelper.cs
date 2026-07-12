using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Saturn.Tests.TestHelpers
{
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

        public string CreateDirectory(string relativePath)
        {
            var fullPath = Path.Combine(_testDirectory, relativePath);
            Directory.CreateDirectory(fullPath);
            _createdDirectories.Add(fullPath);
            return fullPath;
        }

        public string GetPath(string relativePath)
        {
            return Path.Combine(_testDirectory, relativePath);
        }

        public string ReadFile(string relativePath)
        {
            var fullPath = Path.Combine(_testDirectory, relativePath);
            return File.ReadAllText(fullPath);
        }

        public bool FileExists(string relativePath)
        {
            var fullPath = Path.Combine(_testDirectory, relativePath);
            return File.Exists(fullPath);
        }

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
                }
            }

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
                }
            }
        }
    }
}
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Saturn.Skills;

namespace Saturn.Tests.Skills
{
    /// <summary>
    /// Points SATURN_CONFIG_DIR at a fresh temp directory so global skills are
    /// isolated per test class, and clears the workspace skills directory the
    /// tests share via the process working directory.
    /// </summary>
    public abstract class SkillTestBase : IDisposable
    {
        private readonly string? _originalConfigDir;
        protected string TempConfigDir { get; }

        protected SkillTestBase()
        {
            _originalConfigDir = Environment.GetEnvironmentVariable("SATURN_CONFIG_DIR");
            TempConfigDir = Path.Combine(Path.GetTempPath(), $"SaturnSkillTest_{Guid.NewGuid():N}");
            Directory.CreateDirectory(TempConfigDir);
            Environment.SetEnvironmentVariable("SATURN_CONFIG_DIR", TempConfigDir);
            CleanWorkspaceSkills();
        }

        protected static Skill NewSkill(string name, string content = "Some skill content.", params string[] triggers)
        {
            return new Skill
            {
                Name = name,
                Content = content,
                Triggers = triggers.ToList()
            };
        }

        private static void CleanWorkspaceSkills()
        {
            try
            {
                if (Directory.Exists(SkillManager.WorkspaceSkillsDirectory))
                {
                    Directory.Delete(SkillManager.WorkspaceSkillsDirectory, recursive: true);
                }
            }
            catch
            {
            }
        }

        public virtual void Dispose()
        {
            Environment.SetEnvironmentVariable("SATURN_CONFIG_DIR", _originalConfigDir);
            CleanWorkspaceSkills();
            try
            {
                Directory.Delete(TempConfigDir, recursive: true);
            }
            catch
            {
            }
        }
    }
}

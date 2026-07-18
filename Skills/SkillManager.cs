using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Saturn.Skills
{
    /// <summary>
    /// File-backed skill store: one JSON file per skill, in a global directory
    /// (%APPDATA%/Saturn/skills, honoring SATURN_CONFIG_DIR like ConfigurationManager)
    /// and a per-workspace directory (./.saturn/skills). Reads go straight to disk so
    /// the TUI, web UI, and agents always agree without cache invalidation.
    /// </summary>
    public static class SkillManager
    {
        public const int MaxNameLength = 100;
        public const int MaxDescriptionLength = 500;
        public const int MaxContentLength = 20000;
        public const int MaxTriggerLength = 200;
        public const int MaxTriggers = 50;

        private static readonly object WriteLock = new object();

        public static string GlobalSkillsDirectory
        {
            get
            {
                var overrideDir = Environment.GetEnvironmentVariable("SATURN_CONFIG_DIR");
                var baseDir = !string.IsNullOrWhiteSpace(overrideDir)
                    ? overrideDir
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Saturn");
                return Path.Combine(baseDir, "skills");
            }
        }

        /// <summary>
        /// Test seam: workspace skills resolve against the process working
        /// directory, which parallel test collections mutate; tests pin a
        /// stable root here instead.
        /// </summary>
        internal static string? WorkspaceRootOverride { get; set; }

        public static string WorkspaceSkillsDirectory =>
            Path.Combine(WorkspaceRootOverride ?? Saturn.Core.Workspace.WorkspaceManager.CurrentWorkspace, ".saturn", "skills");

        /// <summary>All skills, workspace skills shadowing global ones on a name collision.</summary>
        public static IReadOnlyList<Skill> GetAllSkills()
        {
            var byName = new Dictionary<string, Skill>(StringComparer.OrdinalIgnoreCase);

            foreach (var skill in LoadFromDirectory(GlobalSkillsDirectory, SkillScope.Global))
            {
                byName[skill.Name] = skill;
            }

            foreach (var skill in LoadFromDirectory(WorkspaceSkillsDirectory, SkillScope.Workspace))
            {
                byName[skill.Name] = skill;
            }

            return byName.Values.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static Skill? GetSkillById(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            return GetAllSkills().FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        public static Skill? GetSkillByName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            return GetAllSkills().FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        public static IReadOnlyList<Skill> GetApplicableSkills(SkillAudience audience, string? subAgentTypeName)
        {
            if (audience == SkillAudience.None)
            {
                return Array.Empty<Skill>();
            }

            return GetAllSkills().Where(s => s.AppliesTo(audience, subAgentTypeName)).ToList();
        }

        public static Task<Skill> CreateSkillAsync(Skill skill)
        {
            if (skill == null)
            {
                throw new ArgumentNullException(nameof(skill));
            }

            Normalize(skill);
            Validate(skill);

            lock (WriteLock)
            {
                var existing = GetSkillByName(skill.Name);
                if (existing != null)
                {
                    throw new ArgumentException($"A skill named '{skill.Name}' already exists");
                }

                skill.Id = Guid.NewGuid().ToString("N");
                skill.CreatedAt = DateTime.UtcNow;
                skill.UpdatedAt = DateTime.UtcNow;
                SaveSkillFile(skill);
            }

            return Task.FromResult(skill);
        }

        public static Task<Skill> UpdateSkillAsync(Skill skill)
        {
            if (skill == null)
            {
                throw new ArgumentNullException(nameof(skill));
            }

            Normalize(skill);
            Validate(skill);

            lock (WriteLock)
            {
                var existing = GetSkillById(skill.Id);
                if (existing == null)
                {
                    throw new ArgumentException($"Skill with ID {skill.Id} not found");
                }

                var duplicateName = GetAllSkills().FirstOrDefault(s =>
                    !string.Equals(s.Id, skill.Id, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(s.Name, skill.Name, StringComparison.OrdinalIgnoreCase));
                if (duplicateName != null)
                {
                    throw new ArgumentException($"A skill named '{skill.Name}' already exists");
                }

                skill.CreatedAt = existing.CreatedAt;
                skill.UpdatedAt = DateTime.UtcNow;

                // On a scope change, write the destination before removing the
                // source: a failed save must not leave the skill deleted.
                SaveSkillFile(skill);
                if (existing.Scope != skill.Scope)
                {
                    DeleteSkillFile(existing);
                }
            }

            return Task.FromResult(skill);
        }

        public static Task DeleteSkillAsync(string id)
        {
            lock (WriteLock)
            {
                var existing = GetSkillById(id);
                if (existing == null)
                {
                    throw new ArgumentException($"Skill with ID {id} not found");
                }

                DeleteSkillFile(existing);
            }

            return Task.CompletedTask;
        }

        public static async Task<Skill> DuplicateSkillAsync(string id)
        {
            Skill? original = GetSkillById(id);
            if (original == null)
            {
                throw new ArgumentException($"Skill with ID {id} not found");
            }

            var copy = original.Clone();

            var copyNumber = 1;
            var newName = BuildCopyName(original.Name, "-copy");
            while (GetSkillByName(newName) != null)
            {
                copyNumber++;
                newName = BuildCopyName(original.Name, $"-copy-{copyNumber}");
            }

            copy.Name = newName;
            return await CreateSkillAsync(copy);
        }

        // Trim the base name if needed so the copy suffix never pushes the
        // duplicate's name past the name-length cap.
        private static string BuildCopyName(string baseName, string suffix)
        {
            var available = MaxNameLength - suffix.Length;
            var trimmedBase = baseName.Length > available ? baseName.Substring(0, available) : baseName;
            return $"{trimmedBase.TrimEnd()}{suffix}";
        }

        private static void Normalize(Skill skill)
        {
            skill.Name = (skill.Name ?? string.Empty).Trim();
            skill.Description = (skill.Description ?? string.Empty).Trim();
            skill.Content = (skill.Content ?? string.Empty).Trim();
            skill.Triggers = (skill.Triggers ?? new List<string>())
                .Select(t => (t ?? string.Empty).Trim())
                .Where(t => t.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (skill.SubAgentTypes != null)
            {
                skill.SubAgentTypes = skill.SubAgentTypes
                    .Select(t => (t ?? string.Empty).Trim())
                    .Where(t => t.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (skill.SubAgentTypes.Count == 0)
                {
                    skill.SubAgentTypes = null;
                }
            }
        }

        private static void Validate(Skill skill)
        {
            if (string.IsNullOrWhiteSpace(skill.Name))
            {
                throw new ArgumentException("Skill name cannot be empty");
            }

            if (skill.Name.Length > MaxNameLength)
            {
                throw new ArgumentException($"Skill name too long (max {MaxNameLength} characters)");
            }

            // Names appear in prompts, envelope tags, and file-adjacent UI; keep
            // them to a predictable identifier-like charset.
            if (!skill.Name.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == ' ' || c == '.'))
            {
                throw new ArgumentException("Skill name may only contain letters, digits, spaces, '-', '_' and '.'");
            }

            if (skill.Description.Length > MaxDescriptionLength)
            {
                throw new ArgumentException($"Skill description too long (max {MaxDescriptionLength} characters)");
            }

            if (string.IsNullOrWhiteSpace(skill.Content))
            {
                throw new ArgumentException("Skill content cannot be empty");
            }

            if (skill.Content.Length > MaxContentLength)
            {
                throw new ArgumentException($"Skill content too long (max {MaxContentLength} characters)");
            }

            if (skill.Triggers.Count > MaxTriggers)
            {
                throw new ArgumentException($"Too many triggers (max {MaxTriggers})");
            }

            if (skill.Triggers.Any(t => t.Length > MaxTriggerLength))
            {
                throw new ArgumentException($"Trigger too long (max {MaxTriggerLength} characters)");
            }
        }

        private static IEnumerable<Skill> LoadFromDirectory(string directory, SkillScope scope)
        {
            if (!Directory.Exists(directory))
            {
                yield break;
            }

            string[] files;
            try
            {
                files = Directory.GetFiles(directory, "*.json");
            }
            catch (Exception)
            {
                yield break;
            }

            foreach (var file in files)
            {
                Skill? skill = null;
                try
                {
                    skill = JsonSerializer.Deserialize<Skill>(File.ReadAllText(file));
                }
                catch (Exception)
                {
                    // An unreadable or malformed skill file should not take down
                    // the whole library.
                }

                if (skill == null || string.IsNullOrWhiteSpace(skill.Name))
                {
                    continue;
                }

                // The file name is the identity. Never trust the Id stored inside
                // the file: a skill JSON shipped inside a cloned repo could carry
                // a crafted Id ("..\\..\\evil" or an absolute path) that Save/Delete
                // would otherwise turn into a write or delete outside this directory.
                skill.Id = Path.GetFileNameWithoutExtension(file);
                skill.Scope = scope;

                // Hand-authored files can carry null collections or values past the
                // creation limits; hold them to the same bar as the create path so
                // the matcher and prompts never see a malformed skill.
                bool valid;
                try
                {
                    Normalize(skill);
                    Validate(skill);
                    valid = true;
                }
                catch (Exception)
                {
                    valid = false;
                }

                if (valid)
                {
                    yield return skill;
                }
            }
        }

        private static void SaveSkillFile(Skill skill)
        {
            var directory = skill.Scope == SkillScope.Workspace ? WorkspaceSkillsDirectory : GlobalSkillsDirectory;
            Directory.CreateDirectory(directory);

            var filePath = GetSkillFilePath(directory, skill.Id);
            var json = JsonSerializer.Serialize(skill, new JsonSerializerOptions { WriteIndented = true });

            var tempPath = filePath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, filePath, overwrite: true);
        }

        private static void DeleteSkillFile(Skill skill)
        {
            var directory = skill.Scope == SkillScope.Workspace ? WorkspaceSkillsDirectory : GlobalSkillsDirectory;
            var filePath = GetSkillFilePath(directory, skill.Id);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }

        /// <summary>
        /// Resolves the file for a skill id, refusing any id that would resolve
        /// outside the skills directory (path separators, "..", rooted paths).
        /// </summary>
        private static string GetSkillFilePath(string directory, string id)
        {
            var fileName = $"{id}.json";
            var fullDirectory = Path.GetFullPath(directory);
            var fullPath = Path.GetFullPath(Path.Combine(fullDirectory, fileName));

            if (!string.Equals(Path.GetFileName(fullPath), fileName, StringComparison.Ordinal) ||
                !string.Equals(Path.GetDirectoryName(fullPath), fullDirectory, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Invalid skill id '{id}'");
            }

            return fullPath;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Saturn.Agents.Core
{
    public class ModeManager
    {
        private static readonly Lazy<ModeManager> _instance = new Lazy<ModeManager>(() => new ModeManager());
        private readonly string _modesDirectory;
        private readonly Mode _defaultMode;
        private List<Mode> _modes;

        public static ModeManager Instance => _instance.Value;

        private ModeManager()
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _modesDirectory = Path.Combine(appDataPath, "Saturn", "modes");
            
            if (!Directory.Exists(_modesDirectory))
            {
                Directory.CreateDirectory(_modesDirectory);
            }

            _defaultMode = Mode.CreateDefault();
            _modes = new List<Mode>();
            LoadModes();
        }

        public IReadOnlyList<Mode> GetAllModes()
        {
            var allModes = new List<Mode> { _defaultMode };
            allModes.AddRange(_modes.OrderBy(m => m.Name));
            return allModes;
        }

        public Mode GetMode(Guid modeId)
        {
            if (modeId == Guid.Empty)
                return _defaultMode;
                
            return _modes.FirstOrDefault(m => m.Id == modeId);
        }

        public Mode GetModeByName(string name)
        {
            if (string.Equals(name, "Default", StringComparison.OrdinalIgnoreCase))
                return _defaultMode;
                
            return _modes.FirstOrDefault(m => 
                string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        public async Task<Mode> CreateModeAsync(Mode mode)
        {
            if (mode == null)
                throw new ArgumentNullException(nameof(mode));

            if (string.IsNullOrWhiteSpace(mode.Name))
                throw new ArgumentException("Mode name cannot be empty");

            if (GetModeByName(mode.Name) != null)
                throw new ArgumentException($"A mode with the name '{mode.Name}' already exists");

            mode.Id = Guid.NewGuid();
            mode.CreatedDate = DateTime.UtcNow;
            mode.ModifiedDate = DateTime.UtcNow;
            mode.IsDefault = false;

            _modes.Add(mode);
            await SaveModeAsync(mode);
            
            return mode;
        }

        public async Task<Mode> UpdateModeAsync(Mode mode)
        {
            if (mode == null)
                throw new ArgumentNullException(nameof(mode));

            if (mode.IsDefault)
                throw new InvalidOperationException("Cannot update the default mode");

            if (string.IsNullOrWhiteSpace(mode.Name))
                throw new ArgumentException("Mode name cannot be empty or whitespace");

            if (string.Equals(mode.Name, "Default", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Cannot rename a mode to the reserved 'Default' name");

            var existingMode = _modes.FirstOrDefault(m => m.Id == mode.Id);
            if (existingMode == null)
                throw new ArgumentException($"Mode with ID {mode.Id} not found");

            var duplicateName = _modes.FirstOrDefault(m => 
                m.Id != mode.Id && 
                string.Equals(m.Name, mode.Name, StringComparison.OrdinalIgnoreCase));
                
            if (duplicateName != null)
                throw new ArgumentException($"A mode with the name '{mode.Name}' already exists");

            mode.ModifiedDate = DateTime.UtcNow;
            
            var index = _modes.IndexOf(existingMode);
            _modes[index] = mode;
            
            await SaveModeAsync(mode);
            
            return mode;
        }

        public async Task DeleteModeAsync(Guid modeId)
        {
            if (modeId == Guid.Empty)
                throw new InvalidOperationException("Cannot delete the default mode");

            var mode = _modes.FirstOrDefault(m => m.Id == modeId);
            if (mode == null)
                throw new ArgumentException($"Mode with ID {modeId} not found");

            _modes.Remove(mode);
            
            var filePath = GetModeFilePath(mode.Id);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }

        public async Task<Mode> DuplicateModeAsync(Guid modeId)
        {
            var originalMode = GetMode(modeId);
            if (originalMode == null)
                throw new ArgumentException($"Mode with ID {modeId} not found");

            var duplicatedMode = originalMode.Clone();
            
            var baseName = originalMode.Name.Replace(" (Copy)", "");
            var copyNumber = 1;
            var newName = $"{baseName} (Copy)";
            
            while (GetModeByName(newName) != null)
            {
                copyNumber++;
                newName = $"{baseName} (Copy {copyNumber})";
            }
            
            duplicatedMode.Name = newName;
            
            return await CreateModeAsync(duplicatedMode);
        }

        public void ApplyModeToConfiguration(Guid modeId, AgentConfiguration configuration)
        {
            var mode = GetMode(modeId);
            if (mode == null)
                throw new ArgumentException($"Mode with ID {modeId} not found");

            mode.ApplyToConfiguration(configuration);
        }

        public async Task<Mode> ExportModeAsync(Guid modeId, string exportPath)
        {
            var mode = GetMode(modeId);
            if (mode == null)
                throw new ArgumentException($"Mode with ID {modeId} not found");

            var json = JsonSerializer.Serialize(mode, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            await File.WriteAllTextAsync(exportPath, json);
            return mode;
        }

        public async Task<Mode> ImportModeAsync(string importPath)
        {
            if (!File.Exists(importPath))
                throw new FileNotFoundException($"File not found: {importPath}");

            var json = await File.ReadAllTextAsync(importPath);
            var importedMode = JsonSerializer.Deserialize<Mode>(json);
            
            if (importedMode == null)
                throw new InvalidOperationException("Failed to deserialize mode from file");

            importedMode.Id = Guid.NewGuid();
            importedMode.CreatedDate = DateTime.UtcNow;
            importedMode.ModifiedDate = DateTime.UtcNow;
            importedMode.IsDefault = false;

            var baseName = importedMode.Name;
            var copyNumber = 1;
            var newName = baseName;
            
            while (GetModeByName(newName) != null)
            {
                newName = $"{baseName} ({copyNumber})";
                copyNumber++;
            }
            
            importedMode.Name = newName;
            
            return await CreateModeAsync(importedMode);
        }

        private void LoadModes()
        {
            _modes.Clear();
            
            if (!Directory.Exists(_modesDirectory))
                return;

            var modeFiles = Directory.GetFiles(_modesDirectory, "*.json");
            
            foreach (var file in modeFiles)
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var mode = JsonSerializer.Deserialize<Mode>(json);
                    
                    if (mode != null && mode.Id != Guid.Empty)
                    {
                        mode.IsDefault = false;
                        _modes.Add(mode);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error loading mode from {file}: {ex.Message}");
                }
            }
        }

        private async Task SaveModeAsync(Mode mode)
        {
            if (mode.IsDefault)
                return;

            var filePath = GetModeFilePath(mode.Id);
            var json = JsonSerializer.Serialize(mode, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            await File.WriteAllTextAsync(filePath, json);
        }

        private string GetModeFilePath(Guid modeId)
        {
            return Path.Combine(_modesDirectory, $"{modeId}.json");
        }

        public void RefreshModes()
        {
            LoadModes();
        }
    }
}
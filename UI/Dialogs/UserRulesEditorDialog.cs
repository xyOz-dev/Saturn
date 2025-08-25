using System;
using System.IO;
using System.Threading.Tasks;
using Terminal.Gui;

namespace Saturn.UI.Dialogs
{
    public class UserRulesEditorDialog : Dialog
    {
        private TextView rulesTextView = null!;
        private Label infoLabel = null!;
        private Label statusLabel = null!;
        private Button saveButton = null!;
        private Button cancelButton = null!;
        private Button clearButton = null!;
        private CheckBox enabledCheckBox = null!;
        
        private readonly string rulesFilePath;
        public bool RulesSaved { get; private set; }
        public bool RulesEnabled { get; private set; } = true;
        
        public UserRulesEditorDialog() : base("Edit User Rules", 80, 24)
        {
            ColorScheme = Colors.Dialog;
            rulesFilePath = GetRulesFilePath();
            InitializeComponents();
            _ = LoadExistingRulesAsync();
        }
        
        private void InitializeComponents()
        {
            var headerLabel = new Label("Define custom rules that will be included with every agent interaction:")
            {
                X = 1,
                Y = 1,
                Width = Dim.Fill(1)
            };
            
            infoLabel = new Label("Rules will be included at the end of the system prompt in <user_rules> tags.")
            {
                X = 1,
                Y = 2,
                Width = Dim.Fill(1),
                ColorScheme = new ColorScheme 
                { 
                    Normal = Application.Driver.MakeAttribute(Color.Gray, Color.Black) 
                }
            };
            
            enabledCheckBox = new CheckBox("Enable user rules (uncheck to temporarily disable)")
            {
                X = 1,
                Y = 3,
                Checked = true
            };
            enabledCheckBox.Toggled += (prev) => RulesEnabled = enabledCheckBox.Checked;
            
            statusLabel = new Label("")
            {
                X = 1,
                Y = 4,
                Width = Dim.Fill(1),
                ColorScheme = new ColorScheme 
                { 
                    Normal = Application.Driver.MakeAttribute(Color.Gray, Color.Black) 
                }
            };
            
            var rulesFrame = new FrameView("User Rules (Markdown supported)")
            {
                X = 1,
                Y = 6,
                Width = Dim.Fill(1),
                Height = Dim.Fill(4)
            };
            
            rulesTextView = new TextView()
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill(),
                WordWrap = true,
                AllowsTab = true,
                AllowsReturn = true
            };
            
            rulesFrame.Add(rulesTextView);
            
            saveButton = new Button("_Save Rules", true)
            {
                X = Pos.Center() - 15,
                Y = Pos.AnchorEnd(2)
            };
            saveButton.Clicked += () => _ = SaveRulesAsync();
            
            clearButton = new Button("_Clear All")
            {
                X = Pos.Right(saveButton) + 2,
                Y = Pos.AnchorEnd(2)
            };
            clearButton.Clicked += () => ClearRules();
            
            cancelButton = new Button("_Cancel")
            {
                X = Pos.Right(clearButton) + 2,
                Y = Pos.AnchorEnd(2)
            };
            cancelButton.Clicked += () => Application.RequestStop();
            
            Add(headerLabel, infoLabel, enabledCheckBox, statusLabel, rulesFrame, 
                saveButton, clearButton, cancelButton);
                
            rulesTextView.SetFocus();
        }
        
        private string GetRulesFilePath()
        {
            var saturnDir = Path.Combine(Environment.CurrentDirectory, ".saturn");
            if (!Directory.Exists(saturnDir))
            {
                Directory.CreateDirectory(saturnDir);
            }
            return Path.Combine(saturnDir, "rules.md");
        }
        
        private async Task LoadExistingRulesAsync()
        {
            try
            {
                if (File.Exists(rulesFilePath))
                {
                    var content = await File.ReadAllTextAsync(rulesFilePath);
                    rulesTextView.Text = content;
                    statusLabel.Text = $"Loaded existing rules file ({content.Length} characters)";
                }
                else
                {
                    rulesTextView.Text = GetDefaultRulesTemplate();
                    statusLabel.Text = "No existing rules file found - using template";
                }
            }
            catch (Exception ex)
            {
                statusLabel.Text = $"Error loading rules: {ex.Message}";
                statusLabel.ColorScheme = new ColorScheme 
                { 
                    Normal = Application.Driver.MakeAttribute(Color.BrightRed, Color.Black) 
                };
            }
        }
        
        private string GetDefaultRulesTemplate()
        {
            return @"# User Rules

These rules will be applied to all AI interactions in this workspace.

## General Guidelines
- Follow established code conventions and patterns
- Provide clear and concise explanations
- Focus on maintainable and readable solutions

## Project-Specific Rules
- Add your custom rules here
- Use markdown formatting for clarity
- Examples:
  - Always use async/await for database operations
  - Include XML documentation for public methods
  - Follow specific naming conventions

## Response Format
- Prefer structured responses when appropriate
- Include reasoning for architectural decisions
- Mention potential trade-offs or alternatives

---
*These rules are automatically included in the system prompt for every agent interaction.*";
        }
        
        private async Task SaveRulesAsync()
        {
            try
            {
                var content = rulesTextView.Text.ToString();
                
                if (!enabledCheckBox.Checked)
                {
                    if (File.Exists(rulesFilePath))
                    {
                        try
                        {
                            File.Delete(rulesFilePath);
                        }
                        catch (Exception ex)
                        {
                            statusLabel.Text = $"Error removing rules file: {ex.Message}";
                            statusLabel.ColorScheme = new ColorScheme 
                            { 
                                Normal = Application.Driver.MakeAttribute(Color.BrightRed, Color.Black) 
                            };
                            return;
                        }
                    }
                    statusLabel.Text = "Rules disabled and file removed";
                    RulesSaved = true;
                    Application.RequestStop();
                    return;
                }
                
                if (string.IsNullOrWhiteSpace(content))
                {
                    if (File.Exists(rulesFilePath))
                    {
                        try
                        {
                            File.Delete(rulesFilePath);
                        }
                        catch (Exception ex)
                        {
                            statusLabel.Text = $"Error removing rules file: {ex.Message}";
                            statusLabel.ColorScheme = new ColorScheme 
                            { 
                                Normal = Application.Driver.MakeAttribute(Color.BrightRed, Color.Black) 
                            };
                            return;
                        }
                    }
                    statusLabel.Text = "Empty rules - file removed";
                    RulesSaved = true;
                    Application.RequestStop();
                    return;
                }
                
                // Validation
                if (content.Length > 50000)
                {
                    var result = MessageBox.Query("Content Too Long", 
                        $"Rules content is {content.Length} characters (max 50,000). Truncate?", 
                        "Truncate", "Cancel");
                    
                    if (result == 0)
                    {
                        content = content.Substring(0, 50000);
                        rulesTextView.Text = content;
                    }
                    else
                    {
                        return;
                    }
                }
                
                // Create backup if file exists
                if (File.Exists(rulesFilePath))
                {
                    var backupPath = rulesFilePath + ".backup";
                    File.Copy(rulesFilePath, backupPath, true);
                }
                
                await File.WriteAllTextAsync(rulesFilePath, content);
                
                statusLabel.Text = $"Rules saved successfully ({content.Length} characters)";
                statusLabel.ColorScheme = new ColorScheme 
                { 
                    Normal = Application.Driver.MakeAttribute(Color.BrightGreen, Color.Black) 
                };
                
                RulesSaved = true;
                
                // Auto-close after brief delay
                Application.MainLoop.AddTimeout(TimeSpan.FromMilliseconds(1500), (timer) =>
                {
                    Application.RequestStop();
                    return false;
                });
            }
            catch (Exception ex)
            {
                statusLabel.Text = $"Error saving rules: {ex.Message}";
                statusLabel.ColorScheme = new ColorScheme 
                { 
                    Normal = Application.Driver.MakeAttribute(Color.BrightRed, Color.Black) 
                };
            }
        }
        
        private void ClearRules()
        {
            var result = MessageBox.Query("Clear Rules", 
                "Are you sure you want to clear all rules content?", 
                "Clear", "Cancel");
            
            if (result == 0)
            {
                rulesTextView.Text = "";
                statusLabel.Text = "Rules content cleared";
            }
        }
        
        public override bool ProcessKey(KeyEvent keyEvent)
        {
            if (keyEvent.Key == Key.Esc)
            {
                Application.RequestStop();
                return true;
            }
            
            if (keyEvent.Key == (Key.CtrlMask | Key.S))
            {
                _ = SaveRulesAsync();
                return true;
            }
            
            return base.ProcessKey(keyEvent);
        }
    }
}
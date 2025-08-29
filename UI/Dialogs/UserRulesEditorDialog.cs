using System;
using System.IO;
using System.Threading.Tasks;
using Saturn.Core;
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
        
        public bool RulesSaved { get; private set; }
        public bool RulesEnabled { get; private set; } = true;
        
        public UserRulesEditorDialog(bool initialEnableUserRules = true) : base("Edit User Rules", 80, 24)
        {
            ColorScheme = Colors.Dialog;
            RulesEnabled = initialEnableUserRules;
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
                Checked = RulesEnabled
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
        
        
        private async Task LoadExistingRulesAsync()
        {
            try
            {
                var (content, wasTruncated, error) = await UserRulesManager.LoadUserRules();
                
                if (!string.IsNullOrEmpty(error))
                {
                    statusLabel.Text = $"Error loading rules: {error}";
                    statusLabel.ColorScheme = new ColorScheme 
                    { 
                        Normal = Application.Driver.MakeAttribute(Color.BrightRed, Color.Black) 
                    };
                    rulesTextView.Text = UserRulesManager.GetDefaultRulesTemplate();
                    return;
                }
                
                if (string.IsNullOrEmpty(content))
                {
                    rulesTextView.Text = UserRulesManager.GetDefaultRulesTemplate();
                    statusLabel.Text = "No existing rules file found - using template";
                }
                else
                {
                    rulesTextView.Text = content;
                    var statusText = $"Loaded existing rules file ({content.Length} characters)";
                    if (wasTruncated)
                    {
                        statusText += " - TRUNCATED";
                        statusLabel.ColorScheme = new ColorScheme 
                        { 
                            Normal = Application.Driver.MakeAttribute(Color.BrightYellow, Color.Black) 
                        };
                    }
                    statusLabel.Text = statusText;
                }
            }
            catch (Exception ex)
            {
                statusLabel.Text = $"Error loading rules: {ex.Message}";
                statusLabel.ColorScheme = new ColorScheme 
                { 
                    Normal = Application.Driver.MakeAttribute(Color.BrightRed, Color.Black) 
                };
                rulesTextView.Text = UserRulesManager.GetDefaultRulesTemplate();
            }
        }
        
        
        private async Task SaveRulesAsync()
        {
            try
            {
                var content = rulesTextView.Text.ToString();
                var (maxFileSize, maxContentLength, fileName) = UserRulesManager.GetLimits();
                
                if (!enabledCheckBox.Checked)
                {
                    // Just disable rules without deleting the file
                    RulesEnabled = enabledCheckBox.Checked;
                    statusLabel.Text = "Rules disabled (file preserved)";
                    statusLabel.ColorScheme = new ColorScheme 
                    { 
                        Normal = Application.Driver.MakeAttribute(Color.BrightYellow, Color.Black) 
                    };
                    RulesSaved = true;
                    Application.RequestStop();
                    return;
                }
                
                // Check content length and offer to truncate if too long
                if (content.Length > maxContentLength)
                {
                    var result = MessageBox.Query("Content Too Long", 
                        $"Rules content is {content.Length} characters (max {maxContentLength:N0}). Truncate?", 
                        "Truncate", "Cancel");
                    
                    if (result == 0)
                    {
                        content = content.Substring(0, maxContentLength);
                        rulesTextView.Text = content;
                    }
                    else
                    {
                        return;
                    }
                }
                
                // Use centralized save with backup
                var success = await UserRulesManager.SaveRulesAsync(content, createBackup: true);
                
                if (success)
                {
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
                else
                {
                    statusLabel.Text = "Failed to save rules";
                    statusLabel.ColorScheme = new ColorScheme 
                    { 
                        Normal = Application.Driver.MakeAttribute(Color.BrightRed, Color.Black) 
                    };
                }
            }
            catch (ArgumentException ex)
            {
                // Handle content too long error from UserRulesManager
                statusLabel.Text = ex.Message;
                statusLabel.ColorScheme = new ColorScheme 
                { 
                    Normal = Application.Driver.MakeAttribute(Color.BrightRed, Color.Black) 
                };
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
        
        private void ShowFileInfo()
        {
            var (exists, size, lastModified) = UserRulesManager.GetRulesFileStatus();
            var (maxFileSize, maxContentLength, fileName) = UserRulesManager.GetLimits();
            
            if (exists)
            {
                statusLabel.Text = $"File: {fileName} | Size: {size:N0} bytes | Modified: {lastModified:yyyy-MM-dd HH:mm}";
            }
            else
            {
                statusLabel.Text = $"No rules file exists | Max size: {maxFileSize / 1024 / 1024}MB | Max length: {maxContentLength:N0} chars";
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
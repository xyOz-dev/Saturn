using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Terminal.Gui;
using Saturn.Agents.Core;
using Saturn.Tools.Core;
using Saturn.OpenRouter;
using Saturn.OpenRouter.Models.Api.Models;

namespace Saturn.UI.Dialogs
{
    public class ModeEditorDialog : Dialog
    {
        private OpenRouterClient openRouterClient;
        private TextField nameField;
        private TextField agentNameField;
        private TextField descriptionField;
        private Button selectModelButton;
        private Label modelLabel;
        private TextField temperatureField;
        private TextField maxTokensField;
        private CheckBox streamingCheckBox;
        private CheckBox historyCheckBox;
        private CheckBox approvalCheckBox;
        private CheckBox useDefaultPromptCheckBox;
        private TextView systemPromptTextView;
        private Label toolCountLabel;
        private Button selectToolsButton;
        private Button saveButton;
        private Button cancelButton;
        
        private Mode mode;
        private bool isEditMode;
        private List<string> selectedTools;
        
        public Mode ResultMode { get; private set; }
        
        public ModeEditorDialog(Mode existingMode = null, OpenRouterClient client = null)
            : base(existingMode != null ? $"Edit Mode: {existingMode.Name}" : "Create New Mode", 90, 26)
        {
            ColorScheme = Colors.Dialog;
            openRouterClient = client;
            
            isEditMode = existingMode != null;
            mode = existingMode ?? new Mode();
            selectedTools = new List<string>(mode.ToolNames ?? new List<string>());
            
            InitializeComponents();
            LoadModeData();
        }
        
        private void InitializeComponents()
        {
            var nameLabel = new Label("Mode Name:")
            {
                X = 1,
                Y = 1
            };
            
            nameField = new TextField(mode.Name ?? "")
            {
                X = Pos.Right(nameLabel) + 1,
                Y = 1,
                Width = 30
            };
            
            var agentNameLabel = new Label("Agent Name:")
            {
                X = Pos.Right(nameField) + 3,
                Y = 1
            };
            
            agentNameField = new TextField(mode.AgentName ?? "Assistant")
            {
                X = Pos.Right(agentNameLabel) + 1,
                Y = 1,
                Width = 25
            };
            
            var descriptionLabel = new Label("Description:")
            {
                X = 1,
                Y = 3
            };
            
            descriptionField = new TextField(mode.Description ?? "")
            {
                X = Pos.Right(descriptionLabel) + 1,
                Y = 3,
                Width = Dim.Fill(1)
            };
            
            var modelLabelText = new Label("Model:")
            {
                X = 1,
                Y = 5
            };
            
            modelLabel = new Label(mode.Model ?? "openai/gpt-4o")
            {
                X = Pos.Right(modelLabelText) + 1,
                Y = 5,
                Width = 35,
                ColorScheme = new ColorScheme
                {
                    Normal = Application.Driver.MakeAttribute(Color.Cyan, Color.Black)
                }
            };
            
            selectModelButton = new Button("Select Model...")
            {
                X = Pos.Right(modelLabel) + 2,
                Y = 5
            };
            selectModelButton.Clicked += () => OnSelectModelClicked();
            
            var temperatureLabel = new Label("Temperature:")
            {
                X = 1,
                Y = 7
            };
            
            temperatureField = new TextField(mode.Temperature.ToString("F2"))
            {
                X = Pos.Right(temperatureLabel) + 1,
                Y = 7,
                Width = 8
            };
            
            var maxTokensLabel = new Label("Max Tokens:")
            {
                X = Pos.Right(temperatureField) + 3,
                Y = 7
            };
            
            maxTokensField = new TextField(mode.MaxTokens.ToString())
            {
                X = Pos.Right(maxTokensLabel) + 1,
                Y = 7,
                Width = 10
            };
            
            streamingCheckBox = new CheckBox("Enable Streaming")
            {
                X = 1,
                Y = 9,
                Checked = mode.EnableStreaming
            };
            
            historyCheckBox = new CheckBox("Maintain History")
            {
                X = Pos.Right(streamingCheckBox) + 3,
                Y = 9,
                Checked = mode.MaintainHistory
            };
            
            approvalCheckBox = new CheckBox("Require Command Approval")
            {
                X = Pos.Right(historyCheckBox) + 3,
                Y = 9,
                Checked = mode.RequireCommandApproval
            };
            
            var toolsLabel = new Label("Tools:")
            {
                X = 1,
                Y = 11
            };
            
            toolCountLabel = new Label($"{selectedTools.Count} tools selected")
            {
                X = Pos.Right(toolsLabel) + 1,
                Y = 11
            };
            
            selectToolsButton = new Button("Select Tools...")
            {
                X = Pos.Right(toolCountLabel) + 2,
                Y = 11
            };
            selectToolsButton.Clicked += () => OnSelectToolsClicked();
            
            var promptSeparator = new Label(new string('─', 88))
            {
                X = 0,
                Y = 13,
                Width = Dim.Fill()
            };
            
            useDefaultPromptCheckBox = new CheckBox("Use Default System Prompt")
            {
                X = 1,
                Y = 14,
                Checked = string.IsNullOrWhiteSpace(mode.SystemPromptOverride)
            };
            useDefaultPromptCheckBox.Toggled += (previous) => OnUseDefaultPromptToggled(previous);
            
            var promptLabel = new Label("System Prompt Override:")
            {
                X = 1,
                Y = 15
            };
            
            systemPromptTextView = new TextView()
            {
                X = 1,
                Y = 16,
                Width = Dim.Fill(1),
                Height = 4,
                Text = mode.SystemPromptOverride ?? "",
                ReadOnly = string.IsNullOrWhiteSpace(mode.SystemPromptOverride)
            };
            
            var buttonSeparator = new Label(new string('─', 88))
            {
                X = 0,
                Y = Pos.Bottom(systemPromptTextView) + 1,
                Width = Dim.Fill()
            };
            
            saveButton = new Button("_Save", true)
            {
                X = Pos.Center() - 10,
                Y = Pos.Bottom(buttonSeparator) + 1
            };
            saveButton.Clicked += () => OnSaveClicked();
            
            cancelButton = new Button("_Cancel")
            {
                X = Pos.Right(saveButton) + 2,
                Y = Pos.Top(saveButton)
            };
            cancelButton.Clicked += () => Application.RequestStop();
            
            Add(nameLabel, nameField, agentNameLabel, agentNameField,
                descriptionLabel, descriptionField,
                modelLabelText, modelLabel, selectModelButton, temperatureLabel, temperatureField,
                maxTokensLabel, maxTokensField,
                streamingCheckBox, historyCheckBox, approvalCheckBox,
                toolsLabel, toolCountLabel, selectToolsButton,
                promptSeparator, useDefaultPromptCheckBox, promptLabel, systemPromptTextView,
                buttonSeparator, saveButton, cancelButton);
            
            nameField.SetFocus();
        }
        
        private void LoadModeData()
        {
            UpdateToolCountLabel();
        }
        
        private async void OnSelectModelClicked()
        {
            if (openRouterClient == null)
            {
                MessageBox.ErrorQuery("Error", "OpenRouter client not available", "OK");
                return;
            }
            
            var models = await UI.AgentConfiguration.GetAvailableModels(openRouterClient);
            var modelNames = models.Select(m => m.Name ?? m.Id).ToArray();
            var currentIndex = Array.FindIndex(modelNames, m => models[Array.IndexOf(modelNames, m)].Id == mode.Model);
            if (currentIndex < 0) currentIndex = 0;

            var dialog = new Dialog("Select Model", 60, 20);
            dialog.ColorScheme = Colors.Dialog;

            var listView = new ListView(modelNames)
            {
                X = 1,
                Y = 1,
                Width = Dim.Fill(1),
                Height = Dim.Fill(3),
                SelectedItem = currentIndex
            };

            var infoLabel = new Label("")
            {
                X = 1,
                Y = Pos.Bottom(listView) + 1,
                Width = Dim.Fill(1),
                Height = 1
            };

            Action selectModel = () =>
            {
                var selectedModel = models[listView.SelectedItem];
                mode.Model = selectedModel.Id;
                modelLabel.Text = selectedModel.Id;
                Application.RequestStop();
            };

            listView.SelectedItemChanged += (args) =>
            {
                var selectedModel = models[args.Item];
                var info = $"ID: {selectedModel.Id}";
                if (selectedModel.ContextLength.HasValue)
                    info += $" | Context: {selectedModel.ContextLength:N0} tokens";
                infoLabel.Text = info;
            };

            listView.KeyPress += (e) =>
            {
                if (e.KeyEvent.Key == Key.Enter)
                {
                    selectModel();
                    e.Handled = true;
                }
            };

            var okButton = new Button("OK", true)
            {
                X = Pos.Center() - 10,
                Y = Pos.Bottom(infoLabel) + 1
            };
            okButton.Clicked += () => selectModel();

            var cancelButton = new Button("Cancel")
            {
                X = Pos.Right(okButton) + 2,
                Y = Pos.Top(okButton)
            };
            cancelButton.Clicked += () => Application.RequestStop();

            dialog.Add(listView, infoLabel, okButton, cancelButton);
            
            if (currentIndex >= 0)
            {
                listView.SelectedItem = currentIndex;
                var selectedModel = models[currentIndex];
                var info = $"ID: {selectedModel.Id}";
                if (selectedModel.ContextLength.HasValue)
                    info += $" | Context: {selectedModel.ContextLength:N0} tokens";
                infoLabel.Text = info;
            }
            
            Application.Run(dialog);
        }
        
        private void OnSelectToolsClicked()
        {
            var toolDialog = new ToolSelectionDialog(selectedTools);
            Application.Run(toolDialog);
            
            if (toolDialog.SelectedTools != null)
            {
                selectedTools = toolDialog.SelectedTools;
                UpdateToolCountLabel();
            }
        }
        
        private void UpdateToolCountLabel()
        {
            var allToolsCount = ToolRegistry.Instance.GetAll().Count();
            if (selectedTools.Count == 0)
            {
                toolCountLabel.Text = $"All {allToolsCount} tools enabled";
            }
            else
            {
                toolCountLabel.Text = $"{selectedTools.Count} of {allToolsCount} tools selected";
            }
        }
        
        private void OnUseDefaultPromptToggled(bool previousChecked)
        {
            systemPromptTextView.ReadOnly = useDefaultPromptCheckBox.Checked;
            if (useDefaultPromptCheckBox.Checked)
            {
                systemPromptTextView.Text = "";
            }
        }
        
        private async void OnSaveClicked()
        {
            if (string.IsNullOrWhiteSpace(nameField.Text.ToString()))
            {
                MessageBox.ErrorQuery("Validation Error", "Mode name is required", "OK");
                nameField.SetFocus();
                return;
            }
            
            if (string.IsNullOrWhiteSpace(agentNameField.Text.ToString()))
            {
                MessageBox.ErrorQuery("Validation Error", "Agent name is required", "OK");
                agentNameField.SetFocus();
                return;
            }
            
            if (!double.TryParse(temperatureField.Text.ToString(), out double temperature) || 
                temperature < 0 || temperature > 2)
            {
                MessageBox.ErrorQuery("Validation Error", "Temperature must be between 0 and 2", "OK");
                temperatureField.SetFocus();
                return;
            }
            
            if (!int.TryParse(maxTokensField.Text.ToString(), out int maxTokens) || 
                maxTokens < 1 || maxTokens > 128000)
            {
                MessageBox.ErrorQuery("Validation Error", "Max tokens must be between 1 and 128000", "OK");
                maxTokensField.SetFocus();
                return;
            }
            
            mode.Name = nameField.Text.ToString();
            mode.AgentName = agentNameField.Text.ToString();
            mode.Description = descriptionField.Text.ToString();
            mode.Model = modelLabel.Text.ToString();
            mode.Temperature = temperature;
            mode.MaxTokens = maxTokens;
            mode.EnableStreaming = streamingCheckBox.Checked;
            mode.MaintainHistory = historyCheckBox.Checked;
            mode.RequireCommandApproval = approvalCheckBox.Checked;
            mode.ToolNames = new List<string>(selectedTools);
            
            if (useDefaultPromptCheckBox.Checked)
            {
                mode.SystemPromptOverride = null;
            }
            else
            {
                var promptText = systemPromptTextView.Text.ToString();
                mode.SystemPromptOverride = string.IsNullOrWhiteSpace(promptText) ? null : promptText;
            }
            
            try
            {
                if (isEditMode)
                {
                    ResultMode = await ModeManager.Instance.UpdateModeAsync(mode);
                }
                else
                {
                    ResultMode = await ModeManager.Instance.CreateModeAsync(mode);
                }
                
                Application.RequestStop();
            }
            catch (Exception ex)
            {
                MessageBox.ErrorQuery("Error", $"Failed to save mode: {ex.Message}", "OK");
            }
        }
    }
}
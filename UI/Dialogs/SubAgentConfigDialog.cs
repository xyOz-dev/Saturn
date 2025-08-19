using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Terminal.Gui;
using Saturn.Config;
using Saturn.OpenRouter;
using Saturn.OpenRouter.Models.Api.Models;

namespace Saturn.UI.Dialogs
{
    public class SubAgentConfigDialog : Dialog
    {
        private ComboBox modelComboBox;
        private Label modelInfoLabel;
        private TextField temperatureField;
        private TextField maxTokensField;
        private TextField topPField;
        private CheckBox enableToolsCheckBox;
        private CheckBox enableReviewStageCheckBox;
        private Label reviewerModelLabel;
        private ComboBox reviewerModelComboBox;
        private Label reviewerModelInfoLabel;
        private Button saveButton;
        private Button cancelButton;
        
        private List<Model> availableModels = new();
        private OpenRouterClient? client;
        private SubAgentPreferences preferences;
        
        public bool ConfigurationSaved { get; private set; }
        
        public SubAgentConfigDialog(OpenRouterClient? openRouterClient = null)
            : base("Default Sub-Agent Configuration", 80, 22)
        {
            client = openRouterClient;
            preferences = SubAgentPreferences.Instance;
            
            InitializeComponents();
            _ = LoadModelsAsync();
        }
        
        private void InitializeComponents()
        {
            
            var titleLabel = new Label("Configure default settings for all sub-agents:")
            {
                X = 1,
                Y = 1
            };
            
            var separator1 = new Label(new string('─', 78))
            {
                X = 0,
                Y = 2,
                Width = Dim.Fill()
            };
            
            var modelLabel = new Label("Model:")
            {
                X = 1,
                Y = 3
            };
            
            modelComboBox = new ComboBox()
            {
                X = Pos.Right(modelLabel) + 1,
                Y = 3,
                Width = 50,
                Height = 5
            };
            
            modelInfoLabel = new Label("")
            {
                X = Pos.Right(modelComboBox) + 2,
                Y = 3,
                Width = Dim.Fill(1)
            };
            
            modelComboBox.SetSource(new[] { preferences.DefaultModel });
            modelComboBox.SelectedItemChanged += OnModelChanged;
            
            if (!string.IsNullOrEmpty(preferences.DefaultModel))
            {
                modelComboBox.Text = preferences.DefaultModel;
                modelComboBox.SelectedItem = 0;
            }
            
            var temperatureLabel = new Label("Temperature (0.0-2.0):")
            {
                X = 1,
                Y = 5
            };
            
            temperatureField = new TextField(preferences.DefaultTemperature.ToString("F2"))
            {
                X = Pos.Right(temperatureLabel) + 1,
                Y = 5,
                Width = 10
            };
            
            var maxTokensLabel = new Label("Max Tokens:")
            {
                X = Pos.Right(temperatureField) + 2,
                Y = 5
            };
            
            maxTokensField = new TextField(preferences.DefaultMaxTokens.ToString())
            {
                X = Pos.Right(maxTokensLabel) + 1,
                Y = 5,
                Width = 10
            };
            
            var topPLabel = new Label("Top P (0.0-1.0):")
            {
                X = 1,
                Y = 7
            };
            
            topPField = new TextField(preferences.DefaultTopP.ToString("F2"))
            {
                X = Pos.Right(topPLabel) + 1,
                Y = 7,
                Width = 10
            };
            
            enableToolsCheckBox = new CheckBox("Enable Tools")
            {
                X = Pos.Right(topPField) + 2,
                Y = 7,
                Checked = preferences.DefaultEnableTools
            };
            
            enableReviewStageCheckBox = new CheckBox("Enable Review Stage (Sub-agents' work will be reviewed before completion)")
            {
                X = 1,
                Y = 9,
                Checked = preferences.EnableReviewStage
            };
            
            reviewerModelLabel = new Label("Reviewer Model:")
            {
                X = 1,
                Y = 11,
                Visible = preferences.EnableReviewStage
            };
            
            reviewerModelComboBox = new ComboBox()
            {
                X = Pos.Right(reviewerModelLabel) + 1,
                Y = 11,
                Width = 50,
                Height = 5,
                Visible = preferences.EnableReviewStage
            };
            
            reviewerModelInfoLabel = new Label("")
            {
                X = Pos.Right(reviewerModelComboBox) + 2,
                Y = 11,
                Width = Dim.Fill(1),
                Visible = preferences.EnableReviewStage
            };
            
            reviewerModelComboBox.SetSource(new[] { preferences.ReviewerModel });
            reviewerModelComboBox.SelectedItemChanged += OnReviewerModelChanged;
            
            if (!string.IsNullOrEmpty(preferences.ReviewerModel))
            {
                reviewerModelComboBox.Text = preferences.ReviewerModel;
                reviewerModelComboBox.SelectedItem = 0;
            }
            
            enableReviewStageCheckBox.Toggled += (prev) => 
            {
                var isChecked = enableReviewStageCheckBox.Checked;
                reviewerModelLabel.Visible = isChecked;
                reviewerModelComboBox.Visible = isChecked;
                reviewerModelInfoLabel.Visible = isChecked;
            };
            
            var separator2 = new Label(new string('─', 78))
            {
                X = 0,
                Y = 13,
                Width = Dim.Fill()
            };
            
            var infoLabel = new Label("These settings will be used for all new sub-agents created by the AI assistant.")
            {
                X = 1,
                Y = 14,
                Width = Dim.Fill(1)
            };
            
            saveButton = new Button("_Save Defaults", true)
            {
                X = Pos.Center() - 15,
                Y = 16
            };
            saveButton.Clicked += OnSaveClicked;
            
            cancelButton = new Button("_Cancel")
            {
                X = Pos.Right(saveButton) + 2,
                Y = 16
            };
            cancelButton.Clicked += () => { RequestStop(); };
            
            Add(titleLabel, separator1,
                modelLabel, modelComboBox, modelInfoLabel,
                temperatureLabel, temperatureField,
                maxTokensLabel, maxTokensField,
                topPLabel, topPField, enableToolsCheckBox,
                enableReviewStageCheckBox,
                reviewerModelLabel, reviewerModelComboBox, reviewerModelInfoLabel,
                separator2, infoLabel,
                saveButton, cancelButton);
            
            modelComboBox.SetFocus();
        }
        
        private async Task LoadModelsAsync()
        {
            if (client == null) return;
            
            try
            {
                var response = await client.Models.ListAllAsync();
                if (response?.Data != null)
                {
                    availableModels = response.Data
                        .OrderBy(m => m.Id)
                        .ToList();
                    
                    var modelNames = availableModels.Select(m => m.Id).ToArray();
                    
                    Application.MainLoop.Invoke(() =>
                    {
                        modelComboBox.SetSource(modelNames);
                        reviewerModelComboBox.SetSource(modelNames);
                        
                        var defaultIndex = Array.IndexOf(modelNames, preferences.DefaultModel);
                        if (defaultIndex >= 0)
                        {
                            modelComboBox.SelectedItem = defaultIndex;
                        }
                        
                        var reviewerIndex = Array.IndexOf(modelNames, preferences.ReviewerModel);
                        if (reviewerIndex >= 0)
                        {
                            reviewerModelComboBox.SelectedItem = reviewerIndex;
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Application.MainLoop.Invoke(() =>
                {
                    MessageBox.ErrorQuery("Error", $"Failed to load models: {ex.Message}", "OK");
                });
            }
        }
        
        private void OnModelChanged(ListViewItemEventArgs args)
        {
            if (args.Value == null) return;
            
            var modelId = args.Value.ToString();
            var model = availableModels.FirstOrDefault(m => m.Id == modelId);
            
            if (model != null)
            {
                var contextLengthText = model.ContextLength.HasValue && model.ContextLength.Value > 0 
                    ? $"{model.ContextLength.Value:N0} tokens" 
                    : "Unknown";
                var pricing = model.Pricing != null ? $" | ${model.Pricing.Prompt:F5}/{model.Pricing.Completion:F5}" : "";
                modelInfoLabel.Text = $"Context: {contextLengthText}{pricing}";
            }
        }
        
        private void OnReviewerModelChanged(ListViewItemEventArgs args)
        {
            if (args.Value == null) return;
            
            var modelId = args.Value.ToString();
            var model = availableModels.FirstOrDefault(m => m.Id == modelId);
            
            if (model != null)
            {
                var contextLengthText = model.ContextLength.HasValue && model.ContextLength.Value > 0 
                    ? $"{model.ContextLength.Value:N0} tokens" 
                    : "Unknown";
                var pricing = model.Pricing != null ? $" | ${model.Pricing.Prompt:F5}/{model.Pricing.Completion:F5}" : "";
                reviewerModelInfoLabel.Text = $"Context: {contextLengthText}{pricing}";
            }
        }
        
        private void OnSaveClicked()
        {
            
            if (!int.TryParse(maxTokensField.Text?.ToString(), out var maxTokens) || maxTokens <= 0)
            {
                MessageBox.ErrorQuery("Error", "Max tokens must be a positive number", "OK");
                return;
            }
            
            var selectedModel = string.IsNullOrWhiteSpace(modelComboBox.Text?.ToString()) 
                ? preferences.DefaultModel 
                : modelComboBox.Text.ToString();
            
            if (string.IsNullOrWhiteSpace(selectedModel))
            {
                MessageBox.ErrorQuery("Error", "Please select a model", "OK");
                return;
            }
            
            if (!double.TryParse(temperatureField.Text?.ToString(), out var temperature) || temperature < 0 || temperature > 2)
            {
                MessageBox.ErrorQuery("Error", "Temperature must be between 0.0 and 2.0", "OK");
                return;
            }
            
            if (!double.TryParse(topPField.Text?.ToString(), out var topP) || topP < 0 || topP > 1)
            {
                MessageBox.ErrorQuery("Error", "Top P must be between 0.0 and 1.0", "OK");
                return;
            }
            
            preferences.DefaultModel = selectedModel;
            preferences.DefaultTemperature = temperature;
            preferences.DefaultMaxTokens = maxTokens;
            preferences.DefaultTopP = topP;
            preferences.DefaultEnableTools = enableToolsCheckBox.Checked;
            preferences.EnableReviewStage = enableReviewStageCheckBox.Checked;
            
            if (enableReviewStageCheckBox.Checked)
            {
                var selectedReviewerModel = string.IsNullOrWhiteSpace(reviewerModelComboBox.Text?.ToString()) 
                    ? preferences.ReviewerModel 
                    : reviewerModelComboBox.Text.ToString();
                preferences.ReviewerModel = selectedReviewerModel;
            }
            
            preferences.Save();
            
            ConfigurationSaved = true;
            RequestStop();
        }
    }
}
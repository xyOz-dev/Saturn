using System;
using System.Collections.Generic;
using System.Linq;
using Terminal.Gui;
using Saturn.Providers;

namespace Saturn.UI.Dialogs
{
    public class ProviderSelectionDialog : Dialog
    {
        private RadioGroup providerRadioGroup;
        private Label descriptionLabel;
        private CheckBox saveDefaultCheckbox;
        private Button okButton;
        private Button cancelButton;
        
        public string SelectedProvider { get; private set; }
        public bool SaveAsDefault { get; private set; }
        
        private readonly Dictionary<string, string> providerDescriptions = new()
        {
            ["openrouter"] = "Access multiple AI models through OpenRouter using an API key",
            ["anthropic"] = "Use Claude Pro/Max subscription via OAuth authentication"
        };
        
        public ProviderSelectionDialog() : base("Select LLM Provider")
        {
            InitializeComponents();
        }
        
        private void InitializeComponents()
        {
            Width = 70;
            Height = 15;
            
            // Title label
            var titleLabel = new Label("Choose your preferred LLM provider:")
            {
                X = 1,
                Y = 1
            };
            Add(titleLabel);
            
            // Get available providers
            var providers = ProviderFactory.GetAvailableProviders();
            var providerNames = providers.Select(p => 
            {
                return p switch
                {
                    "openrouter" => "OpenRouter (API Key)",
                    "anthropic" => "Anthropic (Claude Pro/Max)",
                    _ => p
                };
            }).ToArray();
            
            // Radio group for provider selection
            providerRadioGroup = new RadioGroup(providerNames.Select(n => (NStack.ustring)n).ToArray())
            {
                X = 1,
                Y = 3,
                SelectedItem = 0
            };
            
            providerRadioGroup.SelectedItemChanged += (args) =>
            {
                var provider = providers[args.SelectedItem];
                UpdateDescription(provider);
            };
            
            Add(providerRadioGroup);
            
            // Description label
            descriptionLabel = new Label("")
            {
                X = 1,
                Y = 6,
                Width = Dim.Fill() - 2,
                Height = 3,
                TextAlignment = TextAlignment.Left
            };
            Add(descriptionLabel);
            
            // Checkbox for saving as default
            saveDefaultCheckbox = new CheckBox("Save as default provider")
            {
                X = 1,
                Y = 10,
                Checked = true
            };
            Add(saveDefaultCheckbox);
            
            // Buttons
            okButton = new Button("OK")
            {
                X = Pos.Center() - 10,
                Y = 12,
                IsDefault = true
            };
            
            okButton.Clicked += () =>
            {
                SelectedProvider = providers[providerRadioGroup.SelectedItem];
                SaveAsDefault = saveDefaultCheckbox.Checked;
                Application.RequestStop();
            };
            
            cancelButton = new Button("Cancel")
            {
                X = Pos.Center() + 2,
                Y = 12
            };
            
            cancelButton.Clicked += () =>
            {
                SelectedProvider = null;
                Application.RequestStop();
            };
            
            Add(okButton);
            Add(cancelButton);
            
            // Set initial description
            UpdateDescription(providers[0]);
        }
        
        private void UpdateDescription(string provider)
        {
            // Validate input
            if (string.IsNullOrEmpty(provider))
            {
                descriptionLabel.Text = "No provider selected";
                return;
            }
            
            if (string.IsNullOrWhiteSpace(provider))
            {
                descriptionLabel.Text = "Invalid provider name";
                return;
            }
            
            if (providerDescriptions.TryGetValue(provider, out var description))
            {
                descriptionLabel.Text = description;
            }
            else
            {
                descriptionLabel.Text = $"Unknown provider: {provider}";
            }
        }
        
        public static string Show()
        {
            try
            {
                var dialog = new ProviderSelectionDialog();
                Application.Run(dialog);
                
                // Validate returned provider
                if (!string.IsNullOrEmpty(dialog.SelectedProvider))
                {
                    var availableProviders = ProviderFactory.GetAvailableProviders();
                    if (!availableProviders.Contains(dialog.SelectedProvider))
                    {
                        throw new InvalidOperationException($"Selected provider '{dialog.SelectedProvider}' is not available");
                    }
                }
                
                return dialog.SelectedProvider;
            }
            catch (Exception ex)
            {
                // Log error but don't throw - return null to indicate failure
                System.Diagnostics.Debug.WriteLine($"Provider selection failed: {ex.Message}");
                return null;
            }
        }
        
        public static (string provider, bool saveAsDefault) ShowWithOptions()
        {
            try
            {
                var dialog = new ProviderSelectionDialog();
                Application.Run(dialog);
                
                // Validate returned provider
                if (!string.IsNullOrEmpty(dialog.SelectedProvider))
                {
                    var availableProviders = ProviderFactory.GetAvailableProviders();
                    if (!availableProviders.Contains(dialog.SelectedProvider))
                    {
                        throw new InvalidOperationException($"Selected provider '{dialog.SelectedProvider}' is not available");
                    }
                }
                
                return (dialog.SelectedProvider, dialog.SaveAsDefault);
            }
            catch (Exception ex)
            {
                // Log error but don't throw - return null provider to indicate failure
                System.Diagnostics.Debug.WriteLine($"Provider selection with options failed: {ex.Message}");
                return (null, false);
            }
        }
    }
}
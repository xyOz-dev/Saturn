using System;
using System.Threading.Tasks;
using Terminal.Gui;
using Saturn.Providers;
using Saturn.Configuration;

namespace Saturn.UI.Dialogs
{
    public class ProviderSettingsDialog : Dialog
    {
        private Label currentProviderLabel;
        private Label authStatusLabel;
        private Button changeProviderButton;
        private Button reauthenticateButton;
        private Button logoutButton;
        private Button closeButton;
        private ILLMProvider currentProvider;
        
        public bool ProviderChanged { get; private set; }
        
        public ProviderSettingsDialog(ILLMProvider provider) : base("Provider Settings")
        {
            currentProvider = provider;
            InitializeComponents();
        }
        
        private void InitializeComponents()
        {
            Width = 60;
            Height = 16;
            
            // Current provider info
            var providerTitleLabel = new Label("Current Provider:")
            {
                X = 1,
                Y = 1
            };
            Add(providerTitleLabel);
            
            currentProviderLabel = new Label(currentProvider?.Name ?? "None")
            {
                X = 18,
                Y = 1,
                ColorScheme = new ColorScheme
                {
                    Normal = Application.Driver.MakeAttribute(Color.BrightGreen, Color.Black)
                }
            };
            Add(currentProviderLabel);
            
            // Authentication status
            var authTitleLabel = new Label("Auth Status:")
            {
                X = 1,
                Y = 3
            };
            Add(authTitleLabel);
            
            var isAuthenticated = currentProvider?.IsAuthenticated ?? false;
            authStatusLabel = new Label(isAuthenticated ? "Authenticated" : "Not Authenticated")
            {
                X = 18,
                Y = 3,
                ColorScheme = new ColorScheme
                {
                    Normal = Application.Driver.MakeAttribute(
                        isAuthenticated ? Color.BrightGreen : Color.BrightRed, 
                        Color.Black)
                }
            };
            Add(authStatusLabel);
            
            // Auth type
            var authTypeLabel = new Label("Auth Type:")
            {
                X = 1,
                Y = 5
            };
            Add(authTypeLabel);
            
            var authType = currentProvider?.AuthType.ToString() ?? "Unknown";
            var authTypeValueLabel = new Label(authType)
            {
                X = 18,
                Y = 5
            };
            Add(authTypeValueLabel);
            
            // Separator
            var separator = new Label(new string('-', 58))
            {
                X = 1,
                Y = 7
            };
            Add(separator);
            
            // Action buttons
            changeProviderButton = new Button("Change Provider")
            {
                X = 1,
                Y = 9
            };
            
            changeProviderButton.Clicked += async () =>
            {
                var (newProvider, saveAsDefault) = ProviderSelectionDialog.ShowWithOptions();
                if (!string.IsNullOrEmpty(newProvider))
                {
                    await ChangeProviderAsync(newProvider, saveAsDefault);
                }
            };
            
            reauthenticateButton = new Button("Re-authenticate")
            {
                X = 20,
                Y = 9
            };
            
            reauthenticateButton.Clicked += async () =>
            {
                if (currentProvider != null)
                {
                    await ReauthenticateAsync();
                }
            };
            
            logoutButton = new Button("Logout")
            {
                X = 39,
                Y = 9
            };
            
            logoutButton.Clicked += async () =>
            {
                if (currentProvider != null)
                {
                    var result = MessageBox.Query(
                        "Confirm Logout",
                        "Are you sure you want to logout? You will need to authenticate again.",
                        "Yes", "No");
                        
                    if (result == 0)
                    {
                        await LogoutAsync();
                    }
                }
            };
            
            // Provider-specific settings
            if (currentProvider?.Name == "OpenRouter")
            {
                var apiKeyButton = new Button("Update API Key")
                {
                    X = 1,
                    Y = 11
                };
                
                apiKeyButton.Clicked += () =>
                {
                    UpdateApiKey();
                };
                
                Add(apiKeyButton);
            }
            
            // Close button
            closeButton = new Button("Close")
            {
                X = Pos.Center(),
                Y = 13,
                IsDefault = true
            };
            
            closeButton.Clicked += () =>
            {
                Application.RequestStop();
            };
            
            Add(changeProviderButton);
            Add(reauthenticateButton);
            Add(logoutButton);
            Add(closeButton);
        }
        
        private async Task ChangeProviderAsync(string newProviderName, bool saveAsDefault)
        {
            try
            {
                var provider = await ProviderFactory.CreateAndAuthenticateAsync(newProviderName);
                
                if (provider != null)
                {
                    currentProvider = provider;
                    ProviderChanged = true;
                    
                    // Update configuration
                    var config = await ConfigurationManager.LoadConfigurationAsync();
                    if (config == null)
                    {
                        config = new PersistedAgentConfiguration();
                    }
                    
                    // Save provider preference if requested
                    if (saveAsDefault)
                    {
                        await ConfigurationManagerExtensions.SetDefaultProviderAsync(newProviderName);
                    }
                    
                    await ConfigurationManager.SaveConfigurationAsync(config);
                    
                    // Refresh UI
                    currentProviderLabel.Text = provider.Name;
                    UpdateAuthStatus(true);
                    
                    MessageBox.Query("Success", $"Changed provider to {provider.Name}", "OK");
                }
            }
            catch (Exception ex)
            {
                MessageBox.ErrorQuery("Error", $"Failed to change provider: {ex.Message}", "OK");
            }
        }
        
        private async Task ReauthenticateAsync()
        {
            try
            {
                var success = await currentProvider.AuthenticateAsync();
                UpdateAuthStatus(success);
                
                if (success)
                {
                    MessageBox.Query("Success", "Re-authentication successful", "OK");
                }
                else
                {
                    MessageBox.ErrorQuery("Error", "Re-authentication failed", "OK");
                }
            }
            catch (Exception ex)
            {
                MessageBox.ErrorQuery("Error", $"Re-authentication error: {ex.Message}", "OK");
            }
        }
        
        private async Task LogoutAsync()
        {
            try
            {
                await currentProvider.LogoutAsync();
                UpdateAuthStatus(false);
                MessageBox.Query("Logged Out", "Successfully logged out", "OK");
            }
            catch (Exception ex)
            {
                MessageBox.ErrorQuery("Error", $"Logout error: {ex.Message}", "OK");
            }
        }
        
        private void UpdateApiKey()
        {
            var dialog = new Dialog("Update API Key")
            {
                Width = 60,
                Height = 10
            };
            
            var label = new Label("Enter new API Key:")
            {
                X = 1,
                Y = 1
            };
            
            var textField = new TextField("")
            {
                X = 1,
                Y = 3,
                Width = Dim.Fill() - 2,
                Secret = true
            };
            
            var okButton = new Button("OK")
            {
                X = Pos.Center() - 10,
                Y = 5,
                IsDefault = true
            };
            
            okButton.Clicked += async () =>
            {
                var apiKey = textField.Text.ToString();
                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    // Save new API key
                    Environment.SetEnvironmentVariable("OPENROUTER_API_KEY", apiKey);
                    await ReauthenticateAsync();
                }
                Application.RequestStop();
            };
            
            var cancelButton = new Button("Cancel")
            {
                X = Pos.Center() + 2,
                Y = 5
            };
            
            cancelButton.Clicked += () =>
            {
                Application.RequestStop();
            };
            
            dialog.Add(label, textField, okButton, cancelButton);
            Application.Run(dialog);
        }
        
        private void UpdateAuthStatus(bool authenticated)
        {
            authStatusLabel.Text = authenticated ? "Authenticated" : "Not Authenticated";
            authStatusLabel.ColorScheme = new ColorScheme
            {
                Normal = Application.Driver.MakeAttribute(
                    authenticated ? Color.BrightGreen : Color.BrightRed, 
                    Color.Black)
            };
        }
        
        public static bool Show(ILLMProvider provider)
        {
            var dialog = new ProviderSettingsDialog(provider);
            Application.Run(dialog);
            return dialog.ProviderChanged;
        }
    }
}
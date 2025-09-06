using System;
using Terminal.Gui;

namespace Saturn.UI.Dialogs
{
    public class OpenRouterApiKeyDialog : Dialog
    {
        private TextView instructionsView = null!;
        private TextField apiKeyField = null!;
        private Button openBrowserButton = null!;
        private Button pasteButton = null!;
        private Button okButton = null!;
        private Button cancelButton = null!;
        private CheckBox saveKeyCheckbox = null!;
        
        public string ApiKey { get; private set; } = null!;
        public bool SaveKey { get; private set; }
        public bool Success { get; private set; }
        
        public OpenRouterApiKeyDialog() : base("OpenRouter API Key")
        {
            InitializeComponents();
        }
        
        private void InitializeComponents()
        {
            Width = 80;
            Height = 18;
            
            // Title
            var titleLabel = new Label("Enter your OpenRouter API Key")
            {
                X = 1,
                Y = 1
            };
            Add(titleLabel);
            
            // Instructions
            instructionsView = new TextView()
            {
                X = 1,
                Y = 3,
                Width = Dim.Fill() - 2,
                Height = 5,
                ReadOnly = true,
                WordWrap = true,
                Text = "To get an OpenRouter API key:\n" +
                       "1. Click 'Get API Key' to open OpenRouter in your browser\n" +
                       "2. Sign up or log in to your OpenRouter account\n" +
                       "3. Navigate to the API Keys section in your account settings\n" +
                       "4. Create a new API key and copy it\n" +
                       "5. Paste the key in the field below"
            };
            Add(instructionsView);
            
            // API Key input
            var apiKeyLabel = new Label("API Key:")
            {
                X = 1,
                Y = 9
            };
            Add(apiKeyLabel);
            
            apiKeyField = new TextField("")
            {
                X = 10,
                Y = 9,
                Width = Dim.Fill() - 11,
                Secret = true
            };
            
            // Toggle visibility when focused
            apiKeyField.Enter += (args) =>
            {
                apiKeyField.Secret = false;
            };
            
            apiKeyField.Leave += (args) =>
            {
                apiKeyField.Secret = true;
            };
            
            Add(apiKeyField);
            
            // Save checkbox
            saveKeyCheckbox = new CheckBox("Save API key for future sessions")
            {
                X = 1,
                Y = 11,
                Checked = true
            };
            Add(saveKeyCheckbox);
            
            // Buttons
            openBrowserButton = new Button("Get API Key")
            {
                X = 1,
                Y = 13
            };
            
            openBrowserButton.Clicked += () =>
            {
                OpenBrowser();
            };
            
            pasteButton = new Button("Paste from Clipboard")
            {
                X = Pos.Right(openBrowserButton) + 2,
                Y = 13
            };
            
            pasteButton.Clicked += () =>
            {
                try
                {
                    if (Clipboard.TryGetClipboardData(out string clipboardText))
                    {
                        apiKeyField.Text = clipboardText?.Trim() ?? "";
                    }
                    else
                    {
                        MessageBox.ErrorQuery("Error", "Unable to access clipboard", "OK");
                    }
                }
                catch
                {
                    MessageBox.ErrorQuery("Error", "Clipboard access not supported on this platform", "OK");
                }
            };
            
            okButton = new Button("OK")
            {
                X = Pos.Right(pasteButton) + 2,
                Y = 13,
                IsDefault = true
            };
            
            okButton.Clicked += () =>
            {
                var key = apiKeyField.Text.ToString()?.Trim();
                if (string.IsNullOrWhiteSpace(key))
                {
                    MessageBox.ErrorQuery("Error", "Please enter your OpenRouter API key", "OK");
                    return;
                }
                
                // Basic validation for API key format
                if (!key.StartsWith("sk-or-") || key.Length < 20)
                {
                    var result = MessageBox.Query("Warning", 
                        "The API key doesn't appear to be in the expected format (should start with 'sk-or-').\n" +
                        "Do you want to continue anyway?", 
                        "Yes", "No");
                    
                    if (result == 1) // No
                    {
                        return;
                    }
                }
                
                ApiKey = key;
                SaveKey = saveKeyCheckbox.Checked;
                Success = true;
                Application.RequestStop();
            };
            
            cancelButton = new Button("Cancel")
            {
                X = Pos.Right(okButton) + 2,
                Y = 13
            };
            
            cancelButton.Clicked += () =>
            {
                Success = false;
                Application.RequestStop();
            };
            
            Add(openBrowserButton);
            Add(pasteButton);
            Add(okButton);
            Add(cancelButton);
            
            // Focus on the text field
            apiKeyField.SetFocus();
        }
        
        private void OpenBrowser()
        {
            try
            {
                // Try to open the browser
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://openrouter.ai/settings/keys",
                    UseShellExecute = true
                });
                
                MessageBox.Query("Browser", 
                    "Opening OpenRouter in your browser...\n\n" +
                    "After creating your API key, copy it and paste it in the field below.", 
                    "OK");
            }
            catch
            {
                MessageBox.Query("Browser", 
                    "Please open the following URL in your browser:\n\n" +
                    "https://openrouter.ai/settings/keys\n\n" +
                    "After creating your API key, copy it and paste it in the field below.", 
                    "OK");
            }
        }
        
        public static (bool success, string apiKey, bool saveKey) Show()
        {
            var dialog = new OpenRouterApiKeyDialog();
            Application.Run(dialog);
            return (dialog.Success, dialog.ApiKey, dialog.SaveKey);
        }
    }
}
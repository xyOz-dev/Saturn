using System;
using Terminal.Gui;
using Saturn.Providers.Anthropic;

namespace Saturn.UI.Dialogs
{
    public class AnthropicLoginDialog : Dialog
    {
        private TextView instructionsView = null!;
        private TextField codeField = null!;
        private Button loginButton = null!;
        private Button pasteButton = null!;
        private Button okButton = null!;
        private Button cancelButton = null!;
        private RadioGroup authMethodGroup = null!;
        
        public string AuthorizationCode { get; private set; } = null!;
        public bool UseClaudeMax { get; private set; }
        public bool Success { get; private set; }
        
        public AnthropicLoginDialog() : base("Anthropic Authentication")
        {
            InitializeComponents();
        }
        
        private void InitializeComponents()
        {
            Width = 80;
            Height = 20;
            
            // Authentication method selection
            var methodLabel = new Label("Select authentication method:")
            {
                X = 1,
                Y = 1
            };
            Add(methodLabel);
            
            authMethodGroup = new RadioGroup(new NStack.ustring[] 
            { 
                "Claude Pro/Max (Personal subscription)",
                "Create API Key (Requires Anthropic account)"
            })
            {
                X = 1,
                Y = 3,
                SelectedItem = 0
            };
            
            authMethodGroup.SelectedItemChanged += (args) =>
            {
                UpdateInstructions();
            };
            
            Add(authMethodGroup);
            
            // Instructions
            var instructionsLabel = new Label("Instructions:")
            {
                X = 1,
                Y = 7
            };
            Add(instructionsLabel);
            
            instructionsView = new TextView()
            {
                X = 1,
                Y = 8,
                Width = Dim.Fill() - 2,
                Height = 5,
                ReadOnly = true,
                WordWrap = true
            };
            Add(instructionsView);
            
            // Code input
            var codeLabel = new Label("Authorization Code:")
            {
                X = 1,
                Y = 14
            };
            Add(codeLabel);
            
            codeField = new TextField("")
            {
                X = 20,
                Y = 14,
                Width = Dim.Fill() - 21
            };
            Add(codeField);
            
            // Buttons
            loginButton = new Button("Open Browser")
            {
                X = 1,
                Y = 16
            };
            
            loginButton.Clicked += () =>
            {
                UseClaudeMax = authMethodGroup.SelectedItem == 0;
                OpenBrowser();
            };
            
            pasteButton = new Button("Paste from Clipboard")
            {
                X = 15,
                Y = 16
            };
            
            pasteButton.Clicked += () =>
            {
                // Try to get clipboard content
                try
                {
                    if (Clipboard.TryGetClipboardData(out string clipboardText))
                    {
                        codeField.Text = clipboardText;
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
            
            okButton = new Button("Authenticate")
            {
                X = Pos.Right(pasteButton) + 2,
                Y = 16,
                IsDefault = true
            };
            
            okButton.Clicked += () =>
            {
                if (string.IsNullOrWhiteSpace(codeField.Text.ToString()))
                {
                    MessageBox.ErrorQuery("Error", "Please enter the authorization code", "OK");
                    return;
                }
                
                AuthorizationCode = codeField.Text.ToString();
                UseClaudeMax = authMethodGroup.SelectedItem == 0;
                Success = true;
                Application.RequestStop();
            };
            
            cancelButton = new Button("Cancel")
            {
                X = Pos.Right(okButton) + 2,
                Y = 16
            };
            
            cancelButton.Clicked += () =>
            {
                Success = false;
                Application.RequestStop();
            };
            
            Add(loginButton);
            Add(pasteButton);
            Add(okButton);
            Add(cancelButton);
            
            // Set initial instructions
            UpdateInstructions();
        }
        
        private void UpdateInstructions()
        {
            var isClaudeMax = authMethodGroup.SelectedItem == 0;
            
            if (isClaudeMax)
            {
                instructionsView.Text = 
                    "1. Click 'Open Browser' to launch the Claude authentication page\n" +
                    "2. Log in with your Claude Pro/Max account\n" +
                    "3. After authorization, copy the code shown on the page\n" +
                    "4. Paste the code in the field below and click 'Authenticate'";
            }
            else
            {
                instructionsView.Text = 
                    "1. Click 'Open Browser' to open the Anthropic Console\n" +
                    "2. Log in or create an Anthropic account\n" +
                    "3. Navigate to API Keys section and create a new key\n" +
                    "4. Copy the authorization code and paste it below";
            }
        }
        
        private void OpenBrowser()
        {
            // This will be called by the auth service
            // Just show a message here
            var message = UseClaudeMax 
                ? "Opening claude.ai for authentication..." 
                : "Opening console.anthropic.com for API key creation...";
                
            MessageBox.Query("Browser", message + "\n\nIf the browser doesn't open, please check the console for the URL.", "OK");
        }
        
        public static (bool success, string code, bool useClaudeMax) Show()
        {
            var dialog = new AnthropicLoginDialog();
            Application.Run(dialog);
            return (dialog.Success, dialog.AuthorizationCode, dialog.UseClaudeMax);
        }
    }
}
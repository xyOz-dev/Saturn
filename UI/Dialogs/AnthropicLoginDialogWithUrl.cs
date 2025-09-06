using System;
using Terminal.Gui;
using Saturn.Providers.Anthropic;
using Saturn.Providers.Anthropic.Utils;

namespace Saturn.UI.Dialogs
{
    public class AnthropicLoginDialogWithUrl : Dialog
    {
        private TextView instructionsView = null!;
        private TextField codeField = null!;
        private Button loginButton = null!;
        private Button pasteButton = null!;
        private Button okButton = null!;
        private Button cancelButton = null!;
        private RadioGroup authMethodGroup = null!;
        
        private PKCEGenerator.PKCEPair? _currentPKCE;
        private string? _currentStateToken;
        private string? _authUrl;
        
        public string AuthorizationCode { get; private set; } = null!;
        public bool UseClaudeMax { get; private set; }
        public bool Success { get; private set; }
        
        // OAuth Configuration Constants (same as in AnthropicAuthService)
        private const string CLIENT_ID = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";
        private const string AUTH_URL_CLAUDE = "https://claude.ai/oauth/authorize";
        private const string AUTH_URL_CONSOLE = "https://console.anthropic.com/oauth/authorize";
        private const string REDIRECT_URI = "https://console.anthropic.com/oauth/code/callback";
        private const string SCOPES = "org:create_api_key user:profile user:inference";
        
        public AnthropicLoginDialogWithUrl() : base("Anthropic Authentication")
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
                GenerateAuthUrl(); // Regenerate URL when method changes
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
                OpenBrowser();
            };
            
            pasteButton = new Button("Paste from Clipboard")
            {
                X = 15,
                Y = 16
            };
            
            pasteButton.Clicked += () =>
            {
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
                
                // Store the full code (including STATE if present)
                AuthorizationCode = codeField.Text.ToString();
                
                // Store the PKCE verifier with the code for the exchange
                if (_currentPKCE != null)
                {
                    // Append PKCE verifier to the code (will be parsed by auth service)
                    AuthorizationCode = $"{AuthorizationCode}|{_currentPKCE.Verifier}";
                }
                
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
            
            // Generate initial auth URL
            GenerateAuthUrl();
            UpdateInstructions();
        }
        
        private void GenerateAuthUrl()
        {
            // Generate PKCE pair for security
            _currentPKCE = PKCEGenerator.Generate();
            
            // Generate state token for CSRF protection
            _currentStateToken = GenerateStateToken();
            
            UseClaudeMax = authMethodGroup.SelectedItem == 0;
            var baseUrl = UseClaudeMax ? AUTH_URL_CLAUDE : AUTH_URL_CONSOLE;
            
            var parameters = new System.Collections.Generic.Dictionary<string, string>
            {
                ["code"] = "true",
                ["client_id"] = CLIENT_ID,
                ["response_type"] = "code",
                ["redirect_uri"] = REDIRECT_URI,
                ["scope"] = SCOPES,
                ["code_challenge"] = _currentPKCE.Challenge,
                ["code_challenge_method"] = "S256",
                ["state"] = _currentStateToken
            };
            
            var queryString = string.Join("&", 
                parameters.Select(kvp => $"{kvp.Key}={Uri.EscapeDataString(kvp.Value)}"));
            
            _authUrl = $"{baseUrl}?{queryString}";
        }
        
        private string GenerateStateToken()
        {
            var bytes = new byte[32];
            using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }
            return Convert.ToBase64String(bytes)
                .Replace("+", "-")
                .Replace("/", "_")
                .TrimEnd('=');
        }
        
        private void UpdateInstructions()
        {
            var isClaudeMax = authMethodGroup.SelectedItem == 0;
            
            if (isClaudeMax)
            {
                instructionsView.Text = 
                    "1. Click 'Open Browser' to launch the Claude authentication page\n" +
                    "2. Log in with your Claude Pro/Max account\n" +
                    "3. After authorization, copy the ENTIRE code shown (including #STATE)\n" +
                    "4. Paste the code in the field below and click 'Authenticate'";
            }
            else
            {
                instructionsView.Text = 
                    "1. Click 'Open Browser' to open the Anthropic Console\n" +
                    "2. Log in or create an Anthropic account\n" +
                    "3. After authorization, copy the ENTIRE code shown (including #STATE)\n" +
                    "4. Paste the code in the field below and click 'Authenticate'";
            }
        }
        
        private void OpenBrowser()
        {
            if (string.IsNullOrEmpty(_authUrl))
            {
                GenerateAuthUrl();
            }
            
            var message = UseClaudeMax 
                ? "Opening claude.ai for authentication..." 
                : "Opening console.anthropic.com for authentication...";
            
            try
            {
                // Actually open the browser with the OAuth URL
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = _authUrl,
                    UseShellExecute = true
                });
                
                MessageBox.Query("Browser", 
                    message + "\n\n" +
                    "After authentication, copy the ENTIRE authorization code\n" +
                    "(including #STATE if present) and paste it below.", 
                    "OK");
            }
            catch
            {
                // If browser fails to open (common on WSL), show URL in a copyable dialog
                ShowUrlDialog();
            }
        }
        
        private void ShowUrlDialog()
        {
            var urlDialog = new Dialog("Open Browser Manually")
            {
                Width = Dim.Percent(70),
                Height = 12
            };
            
            var instructionLabel = new Label(
                "Unable to open browser automatically (common on WSL).\n\n" +
                "Click 'Copy URL to Clipboard' below, then paste it in your browser.\n" +
                "After authentication, paste the ENTIRE code (including #STATE) below.")
            {
                X = 1,
                Y = 1,
                Width = Dim.Fill() - 2
            };
            urlDialog.Add(instructionLabel);
            
            var copyButton = new Button("Copy URL to Clipboard")
            {
                X = Pos.Center() - 15,
                Y = 6
            };
            copyButton.Clicked += () =>
            {
                try
                {
                    Clipboard.TrySetClipboardData(_authUrl);
                    MessageBox.Query("Success", "URL copied to clipboard!\n\nNow paste it in your browser.", "OK");
                }
                catch
                {
                    // If clipboard fails, show the URL as fallback
                    MessageBox.Query("Copy URL Manually", 
                        $"Unable to copy to clipboard.\n\nPlease copy this URL manually:\n\n{_authUrl}", "OK");
                }
            };
            urlDialog.Add(copyButton);
            
            var okButton = new Button("OK")
            {
                X = Pos.Center() + 5,
                Y = 6
            };
            okButton.Clicked += () => Application.RequestStop();
            urlDialog.Add(okButton);
            
            Application.Run(urlDialog);
        }
        
        public static (bool success, string code, bool useClaudeMax) Show()
        {
            var dialog = new AnthropicLoginDialogWithUrl();
            Application.Run(dialog);
            return (dialog.Success, dialog.AuthorizationCode, dialog.UseClaudeMax);
        }
    }
}
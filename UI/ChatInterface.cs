using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Terminal.Gui;
using Saturn.Agents;
using Saturn.Agents.Core;
using Saturn.OpenRouter;
using Saturn.OpenRouter.Models.Api.Chat;
using Saturn.OpenRouter.Models.Api.Models;

namespace Saturn.UI
{
    public class ChatInterface
    {
        private TextView chatView = null!;
        private TextView inputField = null!;
        private Button sendButton = null!;
        private Toplevel app = null!;
        private FrameView rightPanel = null!;
        private Agent agent;
        private bool isProcessing;
        private CancellationTokenSource? cancellationTokenSource;
        private AgentConfiguration currentConfig;
        private OpenRouterClient? openRouterClient;
        private MarkdownRenderer markdownRenderer;

        public ChatInterface(Agent aiAgent, OpenRouterClient? client = null)
        {
            agent = aiAgent ?? throw new ArgumentNullException(nameof(aiAgent));
            openRouterClient = client ?? agent.Configuration.Client as OpenRouterClient;
            isProcessing = false;
            currentConfig = new AgentConfiguration
            {
                Model = agent.Configuration.Model,
                Temperature = agent.Configuration.Temperature ?? 0.15,
                MaxTokens = agent.Configuration.MaxTokens ?? 4096,
                TopP = agent.Configuration.TopP ?? 0.25,
                EnableStreaming = agent.Configuration.EnableStreaming,
                MaintainHistory = agent.Configuration.MaintainHistory,
                MaxHistoryMessages = agent.Configuration.MaxHistoryMessages ?? 10,
                SystemPrompt = agent.Configuration.SystemPrompt?.ToString() ?? ""
            };
            markdownRenderer = new MarkdownRenderer();
        }

        public void Initialize()
        {
            Application.Init();
            SetupTheme();
            
            var menu = CreateMenu();
            app = CreateMainWindow();
            var mainContainer = CreateChatContainer();
            var inputContainer = CreateInputContainer();
            rightPanel = CreateRightPanel();
            
            SetupScrollBar(mainContainer);
            SetupInputHandlers();
            
            inputContainer.Add(inputField, sendButton);
            app.Add(menu, mainContainer, inputContainer, rightPanel);
            
            SetInitialFocus();
        }

        public void Run()
        {
            Application.Run(app);
            Application.Shutdown();
        }

        private void SetupTheme()
        {
            Colors.Base.Normal = Application.Driver.MakeAttribute(Color.Gray, Color.Black);
            Colors.Base.Focus = Application.Driver.MakeAttribute(Color.White, Color.Black);
            Colors.Base.HotNormal = Application.Driver.MakeAttribute(Color.Gray, Color.Black);
            Colors.Base.HotFocus = Application.Driver.MakeAttribute(Color.BrightMagenta, Color.Black);
            Colors.Menu.Normal = Application.Driver.MakeAttribute(Color.Gray, Color.Black);
            Colors.Menu.Focus = Application.Driver.MakeAttribute(Color.White, Color.Black);
            Colors.Menu.HotNormal = Application.Driver.MakeAttribute(Color.White, Color.Black);
            Colors.Menu.HotFocus = Application.Driver.MakeAttribute(Color.BrightMagenta, Color.Black);
            Colors.Menu.Disabled = Application.Driver.MakeAttribute(Color.DarkGray, Color.Black);
            Colors.Dialog.Normal = Application.Driver.MakeAttribute(Color.Gray, Color.Black);
            Colors.Dialog.Focus = Application.Driver.MakeAttribute(Color.White, Color.Black);
            Colors.Dialog.HotNormal = Application.Driver.MakeAttribute(Color.White, Color.Black);
            Colors.Dialog.HotFocus = Application.Driver.MakeAttribute(Color.BrightMagenta, Color.Black);
            Colors.Error.Normal = Application.Driver.MakeAttribute(Color.Red, Color.Black);
            Colors.Error.Focus = Application.Driver.MakeAttribute(Color.White, Color.Black);
            Colors.Error.HotNormal = Application.Driver.MakeAttribute(Color.BrightRed, Color.Black);
            Colors.Error.HotFocus = Application.Driver.MakeAttribute(Color.BrightRed, Color.Black);
        }

        private MenuBar CreateMenu()
        {
            return new MenuBar(new MenuBarItem[]
            {
                new MenuBarItem("_Options", new MenuItem[]
                {
                    new MenuItem("_Clear Chat", "", () =>
                    {
                        if (chatView != null)
                        {
                            chatView.Text = GetWelcomeMessage();
                            chatView.CursorPosition = new Point(0, 0);
                            agent?.ClearHistory();
                        }
                    }),
                    new MenuItem("_Quit", "", () =>
                    {
                        Application.RequestStop();
                    })
                }),
                new MenuBarItem("_Agent", new MenuItem?[]
                {
                    new MenuItem("_Select Model...", "", async () => await ShowModelSelectionDialog()),
                    new MenuItem("_Temperature...", "", () => ShowTemperatureDialog()),
                    new MenuItem("_Max Tokens...", "", () => ShowMaxTokensDialog()),
                    new MenuItem("Top _P...", "", () => ShowTopPDialog()),
                    null,
                    new MenuItem("_Streaming", "", () => ToggleStreaming()) 
                        { Checked = currentConfig.EnableStreaming },
                    new MenuItem("_Maintain History", "", () => ToggleMaintainHistory()) 
                        { Checked = currentConfig.MaintainHistory },
                    null,
                    new MenuItem("_Edit System Prompt...", "", () => ShowSystemPromptDialog()),
                    new MenuItem("_View Configuration...", "", () => ShowConfigurationDialog())
                }),
            });
        }

        private Toplevel CreateMainWindow()
        {
            return new Toplevel()
            {
                ColorScheme = Colors.Base
            };
        }

        private FrameView CreateChatContainer()
        {
            var mainContainer = new FrameView("AI Chat")
            {
                X = 0,
                Y = 1,
                Width = Dim.Percent(75),
                Height = Dim.Fill(3),
                ColorScheme = Colors.Base
            };

            chatView = new TextView()
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(1),
                Height = Dim.Fill(),
                ReadOnly = true,
                WordWrap = true,
                Text = GetWelcomeMessage(),
                ColorScheme = Colors.Base
            };

            mainContainer.Add(chatView);
            return mainContainer;
        }

        private void SetupScrollBar(FrameView mainContainer)
        {
            var chatScrollBar = new ScrollBarView(chatView, true, false)
            {
                X = Pos.Right(chatView),
                Y = 0,
                Height = Dim.Fill(),
                Width = 1,
                ColorScheme = Colors.Base
            };
            mainContainer.Add(chatScrollBar);
        }

        private FrameView CreateInputContainer()
        {
            var inputContainer = new FrameView("Input (Ctrl+Enter to send)")
            {
                X = 0,
                Y = Pos.AnchorEnd(3),
                Width = Dim.Percent(75),
                Height = 3,
                ColorScheme = Colors.Base
            };

            inputField = new TextView()
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(10),
                Height = Dim.Fill(),
                CanFocus = true,
                ColorScheme = Colors.Base,
                WordWrap = true
            };

            sendButton = new Button("Send")
            {
                X = Pos.Right(inputField) + 1,
                Y = Pos.Center(),
                ColorScheme = Colors.Base
            };

            return inputContainer;
        }

        private FrameView CreateRightPanel()
        {
            var panel = new FrameView("Information")
            {
                X = Pos.Percent(75),
                Y = 1,
                Width = Dim.Fill(),
                Height = Dim.Fill(),
                ColorScheme = Colors.Base
            };

            var infoText = new TextView()
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill(),
                ReadOnly = true,
                WordWrap = true,
                Text = GetPanelContent(),
                ColorScheme = Colors.Base
            };

            panel.Add(infoText);
            return panel;
        }

        private string GetPanelContent()
        {
            var content = "Panel Content\n";
            content += "═════════════\n\n";
            content += "This panel will contain:\n\n";
            content += "• Context information\n";
            content += "• Tool outputs\n";
            content += "• Status updates\n";
            content += "• Additional controls\n\n";
            content += "More features coming soon!";
            return content;
        }

        private void SetupInputHandlers()
        {
            inputField.KeyDown += (e) =>
            {
                if (e.KeyEvent.Key == (Key.CtrlMask | Key.Enter))
                {
                    sendButton.OnClicked();
                    e.Handled = true;
                }
            };

            sendButton.Clicked += async () =>
            {
                if (isProcessing && cancellationTokenSource != null)
                {
                    cancellationTokenSource.Cancel();
                }
                else
                {
                    await ProcessMessage();
                }
            };
        }

        private void SetInitialFocus()
        {
            Application.MainLoop.AddTimeout(TimeSpan.FromMilliseconds(100), (timer) =>
            {
                inputField.SetFocus();
                return false;
            });
        }

        private string GetWelcomeMessage()
        {
            var message = "Welcome to Saturn\n";
            message += "================================\n";
            message += $"Agent: {agent.Name}\n";
            message += $"Model: {agent.Configuration.Model}\n";
            message += $"Streaming: {(agent.Configuration.EnableStreaming ? "Enabled" : "Disabled")}\n";
            message += $"Tools: {(agent.Configuration.EnableTools ? "Enabled" : "Disabled")}\n";
            if (agent.Configuration.EnableTools && agent.Configuration.ToolNames != null && agent.Configuration.ToolNames.Count > 0)
            {
                message += $"Available Tools: {string.Join(", ", agent.Configuration.ToolNames)}\n";
            }
            message += "================================\n";
            message += "Type your message below and press Ctrl+Enter to send.\n";
            message += "Use the Options menu to clear chat or quit.\n\n";
            return message;
        }

        private async Task ProcessMessage()
        {
            if (isProcessing)
                return;

            var message = inputField.Text.ToString();
            if (string.IsNullOrWhiteSpace(message))
                return;

            isProcessing = true;
            cancellationTokenSource = new CancellationTokenSource();

            try
            {
                chatView.Text += $"You: {message}\n";
                inputField.Text = "";
                chatView.CursorPosition = new Point(0, chatView.Lines);

                sendButton.Text = " Stop";
                inputField.ReadOnly = true;
                Application.Refresh();

                chatView.Text += "Assistant: ";
                var startPosition = chatView.Text.Length;
                var responseBuilder = new StringBuilder();

                await Task.Run(async () =>
                {
                    try
                    {
                        if (agent.Configuration.EnableStreaming)
                        {
                            await agent.ExecuteStreamAsync(
                                message,
                                async (chunk) =>
                                {
                                    if (!chunk.IsComplete && !chunk.IsToolCall && !string.IsNullOrEmpty(chunk.Content))
                                    {
                                        responseBuilder.Append(chunk.Content);
                                        Application.MainLoop.Invoke(() =>
                                        {
                                            var currentText = chatView.Text.Substring(0, startPosition);
                                            var renderedResponse = markdownRenderer.RenderToTerminal(responseBuilder.ToString());
                                            chatView.Text = currentText + renderedResponse;
                                            chatView.CursorPosition = new Point(0, chatView.Lines);
                                            Application.Refresh();
                                        });
                                    }
                                },
                                cancellationTokenSource.Token);

                            Application.MainLoop.Invoke(() =>
                            {
                                chatView.Text += "\n\n";
                                chatView.CursorPosition = new Point(0, chatView.Lines);
                            });
                        }
                        else
                        {
                            Message response = await agent.Execute<Message>(message);
                            var responseText = response.Content.ToString();
                            var renderedResponse = markdownRenderer.RenderToTerminal(responseText);

                            Application.MainLoop.Invoke(() =>
                            {
                                chatView.Text += renderedResponse + "\n\n";
                                chatView.CursorPosition = new Point(0, chatView.Lines);
                            });
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        Application.MainLoop.Invoke(() =>
                        {
                            chatView.Text += " [Cancelled]\n\n";
                            chatView.CursorPosition = new Point(0, chatView.Lines);
                        });
                    }
                    catch (Exception ex)
                    {
                        Application.MainLoop.Invoke(() =>
                        {
                            chatView.Text += $"[Error: {ex.Message}]\n\n";
                            chatView.CursorPosition = new Point(0, chatView.Lines);
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                chatView.Text += $"\n[Error processing message: {ex.Message}]\n\n";
            }
            finally
            {
                sendButton.Text = "Send";
                sendButton.Enabled = true;
                inputField.ReadOnly = false;
                isProcessing = false;
                cancellationTokenSource?.Dispose();
                cancellationTokenSource = null;
                inputField.SetFocus();
                Application.Refresh();
            }
        }

        private async Task ShowModelSelectionDialog()
        {
            if (openRouterClient == null) return;
            var models = await AgentConfiguration.GetAvailableModels(openRouterClient);
            var modelNames = models.Select(m => m.Name ?? m.Id).ToArray();
            var currentIndex = Array.FindIndex(modelNames, m => models[Array.IndexOf(modelNames, m)].Id == currentConfig.Model);
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

            Action selectModel = async () =>
            {
                var selectedModel = models[listView.SelectedItem];
                currentConfig.Model = selectedModel.Id;
                await ReconfigureAgent();
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
            
            listView.OpenSelectedItem += (args) =>
            {
                selectModel();
            };

            var okButton = new Button(" _OK ", true)
            {
                X = Pos.Center() - 10,
                Y = Pos.Bottom(infoLabel) + 1
            };

            okButton.Clicked += () => selectModel();

            var cancelButton = new Button(" _Cancel ")
            {
                X = Pos.Center() + 5,
                Y = Pos.Bottom(infoLabel) + 1
            };

            cancelButton.Clicked += () => Application.RequestStop();

            dialog.Add(listView, infoLabel, okButton, cancelButton);
            
            // Show initial selection info
            if (models.Count > 0 && currentIndex >= 0)
            {
                var initialModel = models[currentIndex];
                var info = $"ID: {initialModel.Id}";
                if (initialModel.ContextLength.HasValue)
                    info += $" | Context: {initialModel.ContextLength:N0} tokens";
                infoLabel.Text = info;
            }
            
            listView.SetFocus();
            Application.Run(dialog);
        }

        private void ShowTemperatureDialog()
        {
            var dialog = new Dialog("Set Temperature", 50, 10);
            dialog.ColorScheme = Colors.Dialog;

            var label = new Label($"Temperature (0.0 - 2.0): Current = {currentConfig.Temperature:F2}")
            {
                X = 1,
                Y = 1,
                Width = Dim.Fill(1)
            };

            var textField = new TextField(currentConfig.Temperature.ToString("F2"))
            {
                X = 1,
                Y = 3,
                Width = Dim.Fill(1)
            };

            var okButton = new Button("OK", true)
            {
                X = Pos.Center() - 10,
                Y = 5
            };

            okButton.Clicked += async () =>
            {
                if (double.TryParse(textField.Text.ToString(), out double temp) && temp >= 0 && temp <= 2)
                {
                    currentConfig.Temperature = temp;
                    await ReconfigureAgent();
                    Application.RequestStop();
                }
                else
                {
                    MessageBox.ErrorQuery("Invalid Input", "Please enter a value between 0.0 and 2.0", "OK");
                }
            };

            var cancelButton = new Button("Cancel")
            {
                X = Pos.Center() + 5,
                Y = 5
            };

            cancelButton.Clicked += () => Application.RequestStop();

            dialog.Add(label, textField, okButton, cancelButton);
            textField.SetFocus();
            Application.Run(dialog);
        }

        private void ShowMaxTokensDialog()
        {
            var dialog = new Dialog("Set Max Tokens", 50, 10);
            dialog.ColorScheme = Colors.Dialog;

            var label = new Label($"Max Tokens: Current = {currentConfig.MaxTokens}")
            {
                X = 1,
                Y = 1,
                Width = Dim.Fill(1)
            };

            var textField = new TextField(currentConfig.MaxTokens.ToString())
            {
                X = 1,
                Y = 3,
                Width = Dim.Fill(1)
            };

            var okButton = new Button("OK", true)
            {
                X = Pos.Center() - 10,
                Y = 5
            };

            okButton.Clicked += async () =>
            {
                if (int.TryParse(textField.Text.ToString(), out int tokens) && tokens > 0 && tokens <= 200000)
                {
                    currentConfig.MaxTokens = tokens;
                    await ReconfigureAgent();
                    Application.RequestStop();
                }
                else
                {
                    MessageBox.ErrorQuery("Invalid Input", "Please enter a value between 1 and 200000", "OK");
                }
            };

            var cancelButton = new Button("Cancel")
            {
                X = Pos.Center() + 5,
                Y = 5
            };

            cancelButton.Clicked += () => Application.RequestStop();

            dialog.Add(label, textField, okButton, cancelButton);
            textField.SetFocus();
            Application.Run(dialog);
        }

        private void ShowTopPDialog()
        {
            var dialog = new Dialog("Set Top P", 50, 10);
            dialog.ColorScheme = Colors.Dialog;

            var label = new Label($"Top P (0.0 - 1.0): Current = {currentConfig.TopP:F2}")
            {
                X = 1,
                Y = 1,
                Width = Dim.Fill(1)
            };

            var textField = new TextField(currentConfig.TopP.ToString("F2"))
            {
                X = 1,
                Y = 3,
                Width = Dim.Fill(1)
            };

            var okButton = new Button("OK", true)
            {
                X = Pos.Center() - 10,
                Y = 5
            };

            okButton.Clicked += async () =>
            {
                if (double.TryParse(textField.Text.ToString(), out double topP) && topP >= 0 && topP <= 1)
                {
                    currentConfig.TopP = topP;
                    await ReconfigureAgent();
                    Application.RequestStop();
                }
                else
                {
                    MessageBox.ErrorQuery("Invalid Input", "Please enter a value between 0.0 and 1.0", "OK");
                }
            };

            var cancelButton = new Button("Cancel")
            {
                X = Pos.Center() + 5,
                Y = 5
            };

            cancelButton.Clicked += () => Application.RequestStop();

            dialog.Add(label, textField, okButton, cancelButton);
            textField.SetFocus();
            Application.Run(dialog);
        }

        private async void ToggleStreaming()
        {
            currentConfig.EnableStreaming = !currentConfig.EnableStreaming;
            await ReconfigureAgent();
            var menu = app.Subviews.OfType<MenuBar>().FirstOrDefault();
            if (menu != null)
            {
                var agentMenu = menu.Menus[1];
                var streamingItem = agentMenu.Children.FirstOrDefault(m => m?.Title.ToString().Contains("Streaming") == true);
                if (streamingItem != null)
                    streamingItem.Checked = currentConfig.EnableStreaming;
            }
        }

        private async void ToggleMaintainHistory()
        {
            currentConfig.MaintainHistory = !currentConfig.MaintainHistory;
            await ReconfigureAgent();
            var menu = app.Subviews.OfType<MenuBar>().FirstOrDefault();
            if (menu != null)
            {
                var agentMenu = menu.Menus[1];
                var historyItem = agentMenu.Children.FirstOrDefault(m => m?.Title.ToString().Contains("History") == true);
                if (historyItem != null)
                    historyItem.Checked = currentConfig.MaintainHistory;
            }
        }

        private void ShowSystemPromptDialog()
        {
            var dialog = new Dialog("Edit System Prompt", 70, 20);
            dialog.ColorScheme = Colors.Dialog;

            var textView = new TextView()
            {
                X = 1,
                Y = 1,
                Width = Dim.Fill(1),
                Height = Dim.Fill(3),
                Text = currentConfig.SystemPrompt,
                WordWrap = true
            };

            var okButton = new Button("OK", true)
            {
                X = Pos.Center() - 10,
                Y = Pos.Bottom(textView) + 1
            };

            okButton.Clicked += async () =>
            {
                currentConfig.SystemPrompt = textView.Text.ToString();
                await ReconfigureAgent();
                Application.RequestStop();
            };

            var cancelButton = new Button("Cancel")
            {
                X = Pos.Center() + 5,
                Y = Pos.Bottom(textView) + 1
            };

            cancelButton.Clicked += () => Application.RequestStop();

            dialog.Add(textView, okButton, cancelButton);
            textView.SetFocus();
            Application.Run(dialog);
        }

        private void ShowConfigurationDialog()
        {
            var config = $"Current Agent Configuration\n" +
                        $"===========================\n" +
                        $"Model: {currentConfig.Model}\n" +
                        $"Temperature: {currentConfig.Temperature:F2}\n" +
                        $"Max Tokens: {currentConfig.MaxTokens}\n" +
                        $"Top P: {currentConfig.TopP:F2}\n" +
                        $"Streaming: {(currentConfig.EnableStreaming ? "Enabled" : "Disabled")}\n" +
                        $"Maintain History: {(currentConfig.MaintainHistory ? "Enabled" : "Disabled")}\n" +
                        $"Max History Messages: {currentConfig.MaxHistoryMessages}\n\n" +
                        $"System Prompt:\n{currentConfig.SystemPrompt}";

            MessageBox.Query("Agent Configuration", config, "OK");
        }

        private async Task ReconfigureAgent()
        {
            try
            {
                var newConfig = new Saturn.Agents.Core.AgentConfiguration
                {
                    Name = agent.Name,
                    SystemPrompt = await SystemPrompt.Create(currentConfig.SystemPrompt),
                    Client = openRouterClient,
                    Model = currentConfig.Model,
                    Temperature = currentConfig.Temperature,
                    MaxTokens = currentConfig.MaxTokens,
                    TopP = currentConfig.TopP,
                    MaintainHistory = currentConfig.MaintainHistory,
                    MaxHistoryMessages = currentConfig.MaxHistoryMessages,
                    EnableTools = agent.Configuration.EnableTools,
                    EnableStreaming = currentConfig.EnableStreaming,
                    ToolNames = agent.Configuration.ToolNames
                };

                agent = new Agent(newConfig);
                chatView.Text = GetWelcomeMessage();
                chatView.CursorPosition = new Point(0, 0);
                Application.Refresh();
            }
            catch (Exception ex)
            {
                MessageBox.ErrorQuery("Configuration Error", $"Failed to reconfigure agent: {ex.Message}", "OK");
            }
        }
    }
}
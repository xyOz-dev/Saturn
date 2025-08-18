using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Terminal.Gui;
using Saturn.Agents;
using Saturn.Agents.Core;
using Saturn.Agents.MultiAgent;
using Saturn.Configuration;
using Saturn.Data;
using Saturn.Data.Models;
using Saturn.OpenRouter;
using Saturn.OpenRouter.Models.Api.Chat;
using Saturn.OpenRouter.Models.Api.Models;
using Saturn.UI.Dialogs;

namespace Saturn.UI
{
    public class ChatInterface
    {
        private TextView chatView = null!;
        private TextView inputField = null!;
        private Button sendButton = null!;
        private Toplevel app = null!;
        private FrameView toolCallsPanel = null!;
        private FrameView agentStatusPanel = null!;
        private TextView toolCallsView = null!;
        private TextView agentStatusView = null!;
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
            
            agent.OnToolCall += (toolName, args) => UpdateToolCall(toolName, args);
            currentConfig = new AgentConfiguration
            {
                Model = agent.Configuration.Model,
                Temperature = agent.Configuration.Temperature ?? 0.15,
                MaxTokens = agent.Configuration.MaxTokens ?? 4096,
                TopP = agent.Configuration.TopP ?? 0.25,
                EnableStreaming = agent.Configuration.EnableStreaming,
                MaintainHistory = agent.Configuration.MaintainHistory,
                MaxHistoryMessages = agent.Configuration.MaxHistoryMessages ?? 10,
                SystemPrompt = agent.Configuration.SystemPrompt?.ToString() ?? "",
                EnableTools = agent.Configuration.EnableTools,
                ToolNames = agent.Configuration.ToolNames ?? new List<string>(),
                RequireCommandApproval = agent.Configuration.RequireCommandApproval
            };
            markdownRenderer = new MarkdownRenderer();
            InitializeAgentManager();
        }
        
        private void InitializeAgentManager()
        {
            AgentManager.Instance.Initialize(openRouterClient!);
            
            AgentManager.Instance.OnAgentCreated += (agentId, name) =>
            {
                UpdateAgentStatus("Managing sub-agents", 1, new List<string> { $"{name} ({agentId})" });
            };
            
            AgentManager.Instance.OnAgentStatusChanged += (agentId, name, status) =>
            {
                var agents = AgentManager.Instance.GetAllAgentStatuses();
                var agentList = agents.Select(a => $"{a.Name}: {a.Status}").ToList();
                UpdateAgentStatus("Active", agents.Count(a => !a.IsIdle), agentList);
            };
            
            AgentManager.Instance.OnTaskCompleted += (taskId, result) =>
            {
                Application.MainLoop.Invoke(() =>
                {
                    var timestamp = DateTime.Now.ToString("HH:mm:ss");
                    var currentText = toolCallsView.Text.ToString();
                    
                    var newEntry = $"[{timestamp}] Task Completed: {taskId}\n";
                    newEntry += $"  Agent: {result.AgentName}\n";
                    newEntry += $"  Status: {(result.Success ? "Success" : "Failed")}\n";
                    newEntry += $"  Duration: {result.Duration.TotalSeconds:F1}s\n";
                    newEntry += "───────────────\n";
                    
                    toolCallsView.Text = newEntry + currentText;
                });
            };
        }

        public void Initialize()
        {
            Application.Init();
            SetupTheme();
            
            var menu = CreateMenu();
            app = CreateMainWindow();
            var mainContainer = CreateChatContainer();
            var inputContainer = CreateInputContainer();
            toolCallsPanel = CreateToolCallsPanel();
            agentStatusPanel = CreateAgentStatusPanel();
            
            SetupScrollBar(mainContainer);
            SetupInputHandlers();
            
            inputContainer.Add(inputField, sendButton);
            app.Add(menu, mainContainer, inputContainer, toolCallsPanel, agentStatusPanel);
            
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
                    new MenuItem("_Load Chat...", "", async () => await ShowLoadChatDialog()),
                    new MenuItem("_Clear Chat", "", () =>
                    {
                        if (chatView != null)
                        {
                            if (isProcessing && cancellationTokenSource != null)
                            {
                                cancellationTokenSource.Cancel();
                                cancellationTokenSource?.Dispose();
                                cancellationTokenSource = null;
                            }
                            isProcessing = false;
                            
                            chatView.Text = GetWelcomeMessage();
                            chatView.CursorPosition = new Point(0, 0);
                            
                            inputField.Text = "";
                            inputField.ReadOnly = false;
                            
                            sendButton.Text = "Send";
                            sendButton.Enabled = true;
                            
                            agent?.ClearHistory();
                            
                            toolCallsView.Text = "No tool calls yet...\n";
                            
                            AgentManager.Instance.TerminateAllAgents();
                            
                            UpdateAgentStatus("Ready");
                            
                            inputField.SetFocus();
                            Application.Refresh();
                        }
                    }),
                    null,
                    new MenuItem("_Quit", "", () =>
                    {
                        Application.RequestStop();
                    })
                }),
                new MenuBarItem("_Agent", new MenuItem?[]
                {
                    new MenuItem("_Modes...", "", async () => await ShowModeSelectionDialogAsync()),
                    null,
                    new MenuItem("_Select Model...", "", async () => await ShowModelSelectionDialog()),
                    new MenuItem("_Temperature...", "", () => ShowTemperatureDialog()),
                    new MenuItem("_Max Tokens...", "", () => ShowMaxTokensDialog()),
                    new MenuItem("Top _P...", "", () => ShowTopPDialog()),
                    new MenuItem("Select _Tools...", "", async () => await ShowToolSelectionDialogAsync()),
                    null,
                    new MenuItem("_Sub-Agent Defaults...", "", () => ShowSubAgentDefaultsDialog()),
                    null,
                    new MenuItem("_Streaming", "", () => ToggleStreaming()) 
                        { Checked = currentConfig.EnableStreaming },
                    new MenuItem("_Maintain History", "", () => ToggleMaintainHistory()) 
                        { Checked = currentConfig.MaintainHistory },
                    new MenuItem("_Command Approval", "", () => ToggleCommandApproval()) 
                        { Checked = currentConfig.RequireCommandApproval },
                    new MenuItem("Max _History Messages...", "", () => ShowMaxHistoryDialog()),
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

        private FrameView CreateToolCallsPanel()
        {
            var panel = new FrameView("Tool Calls")
            {
                X = Pos.Percent(75),
                Y = 1,
                Width = Dim.Fill(),
                Height = Dim.Percent(50) - 2,
                ColorScheme = Colors.Base
            };

            toolCallsView = new TextView()
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill(),
                ReadOnly = true,
                WordWrap = true,
                Text = "No tool calls yet...\n",
                ColorScheme = Colors.Base
            };

            panel.Add(toolCallsView);
            return panel;
        }

        private FrameView CreateAgentStatusPanel()
        {
            var panel = new FrameView("Agent Status")
            {
                X = Pos.Percent(75),
                Y = Pos.Percent(50) - 1,
                Width = Dim.Fill(),
                Height = Dim.Fill(),
                ColorScheme = Colors.Base
            };

            agentStatusView = new TextView()
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill(),
                ReadOnly = true,
                WordWrap = true,
                Text = GetInitialAgentStatus(),
                ColorScheme = Colors.Base
            };

            panel.Add(agentStatusView);
            return panel;
        }

        private string GetInitialAgentStatus()
        {
            var status = "Main Agent: Ready\n";
            status += "═════════════════\n\n";
            status += "Status: Idle\n";
            status += "Tasks: 0 pending\n\n";
            status += "Sub-agents:\n";
            status += "• None active\n\n";
            return status;
        }

        public void UpdateToolCall(string toolName, string arguments)
        {
            Application.MainLoop.Invoke(() =>
            {
                var timestamp = DateTime.Now.ToString("HH:mm:ss");
                var currentText = toolCallsView.Text.ToString();
                
                if (currentText == "No tool calls yet...\n")
                {
                    currentText = "";
                }
                
                var summary = GetToolSummary(toolName, arguments);
                var newEntry = $"[{timestamp}] {toolName}: {summary}\n";
                newEntry += "───────────────\n";
                
                toolCallsView.Text = newEntry + currentText;
                
                if (toolCallsView.Text.Length > 5000)
                {
                    toolCallsView.Text = toolCallsView.Text.Substring(0, 4000);
                }
            });
        }
        
        private string GetToolSummary(string toolName, string arguments)
        {
            try
            {
                var registry = Tools.Core.ToolRegistry.Instance;
                var tool = registry.GetTool(toolName);
                if (tool != null)
                {
                    var jsonDoc = JsonDocument.Parse(arguments);
                    var parameters = new Dictionary<string, object>();
                    
                    foreach (var property in jsonDoc.RootElement.EnumerateObject())
                    {
                        parameters[property.Name] = GetJsonValue(property.Value);
                    }
                    
                    return tool.GetDisplaySummary(parameters);
                }
            }
            catch
            {
            }
            
            return toolName;
        }
        
        private object GetJsonValue(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.String:
                    return element.GetString();
                case JsonValueKind.Number:
                    if (element.TryGetInt32(out var intValue))
                        return intValue;
                    if (element.TryGetInt64(out var longValue))
                        return longValue;
                    return element.GetDouble();
                case JsonValueKind.True:
                    return true;
                case JsonValueKind.False:
                    return false;
                case JsonValueKind.Null:
                    return null;
                case JsonValueKind.Array:
                    var list = new List<object>();
                    foreach (var item in element.EnumerateArray())
                    {
                        list.Add(GetJsonValue(item));
                    }
                    return list;
                case JsonValueKind.Object:
                    var dict = new Dictionary<string, object>();
                    foreach (var property in element.EnumerateObject())
                    {
                        dict[property.Name] = GetJsonValue(property.Value);
                    }
                    return dict;
                default:
                    return element.ToString();
            }
        }

        public void UpdateAgentStatus(string status, int activeTasks = 0, List<string>? subAgents = null)
        {
            Application.MainLoop.Invoke(() =>
            {
                var agents = AgentManager.Instance.GetAllAgentStatuses();
                var completedTasks = agents.Sum(a => a.CurrentTask != null ? 1 : 0);
                var currentCount = AgentManager.Instance.GetCurrentAgentCount();
                var maxCount = AgentManager.Instance.GetMaxConcurrentAgents();
                
                var statusText = $"Main Agent: {status}\n";
                statusText += "═════════════════\n\n";
                statusText += $"Status: {status}\n";
                statusText += $"Active Tasks: {activeTasks}\n";
                statusText += $"Total Agents: {currentCount}/{maxCount}\n\n";
                statusText += "Sub-agents:\n";
                
                if (agents.Any())
                {
                    foreach (var agent in agents)
                    {
                        statusText += $"• {agent.Name}\n";
                        statusText += $"  Status: {agent.Status}\n";
                        if (!string.IsNullOrEmpty(agent.CurrentTask))
                        {
                            statusText += $"  Task: {agent.CurrentTask}\n";
                            statusText += $"  Time: {agent.RunningTime.TotalSeconds:F1}s\n";
                        }
                    }
                }
                else if (subAgents != null && subAgents.Count > 0)
                {
                    foreach (var agent in subAgents)
                    {
                        statusText += $"• {agent}\n";
                    }
                }
                else
                {
                    statusText += "• None active\n";
                }
                
                agentStatusView.Text = statusText;
            });
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
                    
                    AgentManager.Instance.TerminateAllAgents();
                    
                    sendButton.Text = "Send";
                    sendButton.Enabled = true;
                    inputField.ReadOnly = false;
                    isProcessing = false;
                    
                    UpdateAgentStatus("Cancelled");
                    
                    chatView.Text += " [Cancelled]\n\n";
                    ScrollChatToBottom();
                    
                    inputField.SetFocus();
                    Application.Refresh();
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
        
        private void ScrollChatToBottom()
        {
            if (chatView != null && chatView.Lines > 0)
            {
                var lastLine = Math.Max(0, chatView.Lines - 1);
                chatView.CursorPosition = new Point(0, lastLine);
                
                if (chatView.Frame.Height > 0)
                {
                    var topRow = Math.Max(0, chatView.Lines - chatView.Frame.Height);
                    chatView.TopRow = topRow;
                }
                
                chatView.PositionCursor();
            }
        }

        private string GetWelcomeMessage()
        {
            var message = "Welcome to Saturn\nDont forget to join our discord to stay updated.\n\"https://discord.gg/VSjW36MfYZ\"";
            message += "\n================================\n";
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
                if (agent.CurrentSessionId == null)
                {
                    await agent.InitializeSessionAsync("main");
                    
                    if (agent.CurrentSessionId != null)
                    {
                        AgentManager.Instance.SetParentSessionId(agent.CurrentSessionId);
                    }
                }
                
                chatView.Text += $"You: {message}\n";
                inputField.Text = "";
                
                sendButton.Text = " Stop";
                inputField.ReadOnly = true;
                UpdateAgentStatus("Processing", 1);
                
                ScrollChatToBottom();
                Application.Refresh();
                ScrollChatToBottom();

                chatView.Text += "\nAssistant: ";
                ScrollChatToBottom();
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
                                            ScrollChatToBottom();
                                            Application.Refresh();
                                        });
                                    }
                                },
                                cancellationTokenSource.Token);

                            Application.MainLoop.Invoke(() =>
                            {
                                var currentText = chatView.Text;
                                var lastResponse = responseBuilder.ToString().TrimEnd();
                                
                                if (!string.IsNullOrEmpty(lastResponse))
                                {
                                    bool endsWithNewline = currentText.EndsWith("\n");
                                    bool endsWithDoubleNewline = currentText.EndsWith("\n\n");
                                    
                                    char lastChar = lastResponse.Length > 0 ? lastResponse[lastResponse.Length - 1] : '\0';
                                    bool endsWithPunctuation = ".!?:;)]}\"'`".Contains(lastChar);
                                    
                                    if (!endsWithNewline)
                                    {
                                        if (endsWithPunctuation)
                                        {
                                            chatView.Text += "\n\n";
                                        }
                                        else
                                        {
                                            chatView.Text += " ";
                                        }
                                    }
                                    else if (!endsWithDoubleNewline)
                                    {
                                        chatView.Text += "\n";
                                    }
                                }
                                else
                                {
                                    if (!currentText.EndsWith("\n\n"))
                                    {
                                        if (currentText.EndsWith("\n"))
                                            chatView.Text += "\n";
                                        else
                                            chatView.Text += "\n\n";
                                    }
                                }
                                
                                ScrollChatToBottom();
                            });
                        }
                        else
                        {
                            Message response = await agent.Execute<Message>(message);
                            var responseText = response.Content.ToString();
                            var renderedResponse = markdownRenderer.RenderToTerminal(responseText);

                            Application.MainLoop.Invoke(() =>
                            {
                                var currentText = chatView.Text;
                                
                                chatView.Text += renderedResponse;
                                
                                var updatedText = chatView.Text;
                                bool endsWithNewline = updatedText.EndsWith("\n\n");
                                bool endsWithDoubleNewline = updatedText.EndsWith("\n\n");
                                
                                var trimmedResponse = renderedResponse.TrimEnd();
                                char lastChar = trimmedResponse.Length > 0 ? trimmedResponse[trimmedResponse.Length - 1] : '\0';
                                bool endsWithPunctuation = ".!?:;)]}\"'`".Contains(lastChar);
                                
                                if (!endsWithNewline)
                                {
                                    if (endsWithPunctuation)
                                    {
                                        chatView.Text += "\n\n";
                                    }
                                    else
                                    {
                                        chatView.Text += " ";
                                    }
                                }
                                else if (!endsWithDoubleNewline)
                                {
                                    chatView.Text += "\n";
                                }
                                
                                ScrollChatToBottom();
                            });
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        Application.MainLoop.Invoke(() =>
                        {
                            chatView.Text += " [Cancelled]\n\n";
                            ScrollChatToBottom();
                        });
                    }
                    catch (Exception ex)
                    {
                        Application.MainLoop.Invoke(() =>
                        {
                            chatView.Text += $"[Error: {ex.Message}]\n\n";
                            ScrollChatToBottom();
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                chatView.Text += $"\n[Error processing message: {ex.Message}]\n\n";
                ScrollChatToBottom();
            }
            finally
            {
                sendButton.Text = "Send";
                sendButton.Enabled = true;
                inputField.ReadOnly = false;
                isProcessing = false;
                cancellationTokenSource?.Dispose();
                cancellationTokenSource = null;
                UpdateAgentStatus("Ready");
                inputField.SetFocus();
                Application.Refresh();
            }
        }

        /// <summary>
        /// Displays a modal dialog that lets the user select a model from the list provided by the OpenRouter client and applies the selection to the current UI configuration.
        /// </summary>
        /// <remarks>
        /// - Requires <c>openRouterClient</c>; if it's null the method returns immediately.
        /// - Fetches available models, shows their names and basic info (ID and optional context length), and updates <c>currentConfig.Model</c> when the user confirms.
        /// - If a GPT-5 model is chosen, forces <c>currentConfig.Temperature</c> to 1.0 before applying the configuration.
        /// - Applies changes by calling <see cref="UpdateConfiguration"/>, which persists the configuration and refreshes the UI, then closes the dialog.
        /// </remarks>
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
                
                if (selectedModel.Id.Contains("gpt-5", StringComparison.OrdinalIgnoreCase))
                {
                    currentConfig.Temperature = 1.0;
                }
                
                await UpdateConfiguration();
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

        /// <summary>
        /// Shows a modal dialog allowing the user to set the agent temperature (0.0–2.0) and applies the change.
        /// </summary>
        /// <remarks>
        /// If the current model name contains "gpt-5" (case-insensitive), the temperature is locked to 1.0 and the dialog is not shown.
        /// The entered value is validated to be within [0.0, 2.0]; on success the value is stored to <c>currentConfig.Temperature</c>
        /// and <c>UpdateConfiguration()</c> is invoked to persist and apply the change. Invalid input displays an error message.
        /// </remarks>
        private void ShowTemperatureDialog()
        {
            if (currentConfig.Model.Contains("gpt-5", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.ErrorQuery("Temperature Locked", 
                    "Temperature is locked to 1.0 for GPT-5 models and cannot be changed.", "OK");
                return;
            }
            
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
                    await UpdateConfiguration();
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

        private void ShowSubAgentDefaultsDialog()
        {
            var dialog = new SubAgentConfigDialog(openRouterClient);
            Application.Run(dialog);
            
            if (dialog.ConfigurationSaved)
            {
                MessageBox.Query("Success", "Default sub-agent configuration saved.", "OK");
            }
        }
        
        /// <summary>
        /// Shows a modal dialog that lets the user set the agent's maximum token limit.
        /// </summary>
        /// <remarks>
        /// Presents a dialog with the current value, accepts an integer input (1–200000), validates it,
        /// and on valid input updates <c>currentConfig.MaxTokens</c> and persists the configuration via <see cref="UpdateConfiguration"/>.
        /// Displays an error message for invalid values and closes the dialog on successful update or Cancel.
        /// The dialog is run modally and sets focus to the input field when opened.
        /// </remarks>
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
                    await UpdateConfiguration();
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

        /// <summary>
        /// Displays a modal dialog that lets the user view and set the agent's Top P temperature (0.0–1.0).
        /// </summary>
        /// <remarks>
        /// Validates input to the range [0.0, 1.0]; on success it updates the in-memory configuration (currentConfig.TopP),
        /// persists the change via UpdateConfiguration(), and closes the dialog. If the input is invalid, an error message is shown
        /// and the dialog remains open.
        /// </remarks>
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
                    await UpdateConfiguration();
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

        /// <summary>
        /// Shows a modal dialog that lets the user set the maximum number of messages to retain in chat history (0–100).
        /// </summary>
        /// <remarks>
        /// Valid input updates <c>currentConfig.MaxHistoryMessages</c>, persists the configuration via <c>UpdateConfiguration()</c>,
        /// and closes the dialog. Invalid input displays an error and keeps the dialog open. Cancelling closes the dialog without changes.
        /// </remarks>
        private void ShowMaxHistoryDialog()
        {
            var dialog = new Dialog("Set Max History Messages", 50, 10);
            dialog.ColorScheme = Colors.Dialog;

            var label = new Label($"Max History Messages (0-100): Current = {currentConfig.MaxHistoryMessages}")
            {
                X = 1,
                Y = 1,
                Width = Dim.Fill(1)
            };

            var textField = new TextField(currentConfig.MaxHistoryMessages.ToString())
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
                if (int.TryParse(textField.Text.ToString(), out int maxHistory) && maxHistory >= 0 && maxHistory <= 100)
                {
                    currentConfig.MaxHistoryMessages = maxHistory;
                    await UpdateConfiguration();
                    Application.RequestStop();
                }
                else
                {
                    MessageBox.ErrorQuery("Invalid Input", "Please enter a value between 0 and 100", "OK");
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

        /// <summary>
        /// Toggle the streaming setting for the current UI configuration, persist the change, and update the Agent menu's "Streaming" checkbox.
        /// </summary>
        /// <remarks>
        /// Flips <c>currentConfig.EnableStreaming</c>, calls <c>UpdateConfiguration()</c> to apply and save the change, and then synchronizes the Agent menu item's Checked state to reflect the new value.
        /// </remarks>
        private async void ToggleStreaming()
        {
            currentConfig.EnableStreaming = !currentConfig.EnableStreaming;
            await UpdateConfiguration();
            var menu = app.Subviews.OfType<MenuBar>().FirstOrDefault();
            if (menu != null)
            {
                var agentMenu = menu.Menus[1];
                var streamingItem = agentMenu.Children.FirstOrDefault(m => m?.Title.ToString().Contains("Streaming") == true);
                if (streamingItem != null)
                    streamingItem.Checked = currentConfig.EnableStreaming;
            }
        }

        /// <summary>
        /// Toggle the MaintainHistory flag on the current UI configuration, persist the change, and update the Agent menu's "History" checkbox to match.
        /// </summary>
        /// <remarks>
        /// This method flips <c>currentConfig.MaintainHistory</c>, calls <c>UpdateConfiguration()</c> to apply and save the change, and then finds the Agent menu entry whose title contains "History" to set its checked state to the new value. Intended to be invoked from UI event handlers.
        /// </remarks>
        private async void ToggleMaintainHistory()
        {
            currentConfig.MaintainHistory = !currentConfig.MaintainHistory;
            await UpdateConfiguration();
            var menu = app.Subviews.OfType<MenuBar>().FirstOrDefault();
            if (menu != null)
            {
                var agentMenu = menu.Menus[1];
                var historyItem = agentMenu.Children.FirstOrDefault(m => m?.Title.ToString().Contains("History") == true);
                if (historyItem != null)
                    historyItem.Checked = currentConfig.MaintainHistory;
            }
        }

        /// <summary>
        /// Toggle the RequireCommandApproval flag in the current UI configuration, persist the change,
        /// and update the Agent menu item checkbox to reflect the new state.
        /// </summary>
        /// <remarks>
        /// This updates currentConfig.RequireCommandApproval, calls UpdateConfiguration to apply and save the change,
        /// and then locates the "Command Approval" menu item (if present) to set its Checked state to match the configuration.
        /// </remarks>
        private async void ToggleCommandApproval()
        {
            currentConfig.RequireCommandApproval = !currentConfig.RequireCommandApproval;
            await UpdateConfiguration();
            var menu = app.Subviews.OfType<MenuBar>().FirstOrDefault();
            if (menu != null)
            {
                var agentMenu = menu.Menus[1];
                var commandApprovalItem = agentMenu.Children?.FirstOrDefault(m => m?.Title?.ToString()?.Contains("Command Approval") == true);
                if (commandApprovalItem != null)
                    commandApprovalItem.Checked = currentConfig.RequireCommandApproval;
            }
        }

        /// <summary>
        /// Opens a modal dialog that lets the user edit the agent's system prompt and saves the change.
        /// </summary>
        /// <remarks>
        /// Displays a text editor initialized with the current system prompt. If the user confirms (OK),
        /// the new prompt is written into <c>currentConfig.SystemPrompt</c>, <see cref="UpdateConfiguration"/> is invoked
        /// to persist and apply the change, and the dialog is closed. Cancelling closes the dialog without saving.
        /// </remarks>
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
                await UpdateConfiguration();
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

        /// <summary>
        /// Displays the mode selection dialog and applies the user's choice to the current UI configuration.
        /// </summary>
        /// <remarks>
        /// If the user selects a mode, the method maps that mode into the UI configuration, persists the change by calling <see cref="UpdateConfiguration"/>, and updates the UI. 
        /// If the user chooses to create a new mode, the mode editor is opened. If the user chooses to edit an existing mode, the editor is opened with that mode preloaded.
        /// Any exception thrown while applying a selected mode is caught and reported in an error dialog.
        /// </remarks>
        private async Task ShowModeSelectionDialogAsync()
        {
            var dialog = new ModeSelectionDialog();
            Application.Run(dialog);
            
            if (dialog.SelectedMode != null)
            {
                try
                {
                    ApplyModeToUIConfiguration(dialog.SelectedMode);
                    await UpdateConfiguration();
                }
                catch (Exception ex)
                {
                    MessageBox.ErrorQuery("Error", $"Failed to apply mode: {ex.Message}", "OK");
                }
            }
            else if (dialog.ShouldCreateNew)
            {
                await ShowModeEditorDialogAsync(null);
            }
            else if (dialog.ModeToEdit != null)
            {
                await ShowModeEditorDialogAsync(dialog.ModeToEdit);
            }
        }
        
        /// <summary>
        /// Opens the mode editor dialog to create or edit a Mode. If the user saves a mode,
        /// shows a success message and optionally applies the mode immediately to the UI configuration.
        /// </summary>
        /// <param name="modeToEdit">The Mode to edit, or null to create a new mode.</param>
        private async Task ShowModeEditorDialogAsync(Mode modeToEdit)
        {
            var editorDialog = new ModeEditorDialog(modeToEdit, openRouterClient);
            Application.Run(editorDialog);
            
            if (editorDialog.ResultMode != null)
            {
                var message = modeToEdit != null 
                    ? $"Mode '{editorDialog.ResultMode.Name}' updated successfully"
                    : $"Mode '{editorDialog.ResultMode.Name}' created successfully";
                    
                MessageBox.Query("Success", message, "OK");
                
                var applyNow = MessageBox.Query("Apply Mode", 
                    $"Would you like to apply the mode '{editorDialog.ResultMode.Name}' now?", 
                    "Yes", "No");
                    
                if (applyNow == 0)
                {
                    ApplyModeToUIConfiguration(editorDialog.ResultMode);
                    await UpdateConfiguration();
                }
            }
        }
        
        private void ApplyModeToUIConfiguration(Mode mode)
        {
            currentConfig.Model = mode.Model;
            currentConfig.Temperature = mode.Temperature;
            currentConfig.MaxTokens = mode.MaxTokens;
            currentConfig.TopP = mode.TopP;
            currentConfig.EnableStreaming = mode.EnableStreaming;
            currentConfig.MaintainHistory = mode.MaintainHistory;
            currentConfig.RequireCommandApproval = mode.RequireCommandApproval;
            currentConfig.ToolNames = new List<string>(mode.ToolNames ?? new List<string>());
            currentConfig.EnableTools = mode.ToolNames?.Count > 0;
            
            if (!string.IsNullOrWhiteSpace(mode.SystemPromptOverride))
            {
                currentConfig.SystemPrompt = mode.SystemPromptOverride;
            }
        }
        
        /// <summary>
        /// Displays a modal tool-selection dialog and, if the user changes the selection, updates and persists the agent's tool configuration.
        /// </summary>
        /// <remarks>
        /// Opens a ToolSelectionDialog initialized with the current tool names. If the dialog results in a non-empty selection or there were previously configured tools,
        /// this method assigns the selected tools to <c>currentConfig.ToolNames</c>, sets <c>currentConfig.EnableTools</c> accordingly, and calls <see cref="UpdateConfiguration"/> to persist and apply the changes.
        /// </remarks>
        /// <returns>A task that completes after the dialog closes and any configuration updates have been applied.</returns>
        private async Task ShowToolSelectionDialogAsync()
        {
            var dialog = new ToolSelectionDialog(currentConfig.ToolNames);
            Application.Run(dialog);
            
            if (dialog.SelectedTools.Count > 0 || currentConfig.ToolNames?.Count > 0)
            {
                currentConfig.ToolNames = dialog.SelectedTools;
                currentConfig.EnableTools = dialog.SelectedTools.Count > 0;
                await UpdateConfiguration();
            }
        }

        /// <summary>
        /// Shows a modal dialog summarizing the current agent configuration.
        /// </summary>
        /// <remarks>
        /// Displays a read-only, human-friendly summary (model, temperature, max tokens, Top P, streaming/history/command-approval flags,
        /// max history messages, tool enablement and selected tool count, and the system prompt) in a blocking message box.
        /// This method only presents the in-memory <c>currentConfig</c> and does not modify application state.
        /// </remarks>
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
                        $"Command Approval: {(currentConfig.RequireCommandApproval ? "Enabled" : "Disabled")}\n" +
                        $"Max History Messages: {currentConfig.MaxHistoryMessages}\n" +
                        $"Tools: {(currentConfig.EnableTools ? $"Enabled ({currentConfig.ToolNames?.Count ?? 0} selected)" : "Disabled")}\n\n" +
                        $"System Prompt:\n{currentConfig.SystemPrompt}";

            MessageBox.Query("Agent Configuration", config, "OK");
        }

        /// <summary>
        /// Applies the current UI configuration to the live agent, persists it, and refreshes the UI.
        /// </summary>
        /// <remarks>
        /// - Copies values from <c>currentConfig</c> into <c>agent.Configuration</c>, including model, temperature,
        ///   token limits, sampling parameters, history and tool settings, and command-approval preference.
        /// - If the selected model name contains "gpt-5" (case-insensitive), the temperature is forced to 1.0.
        /// - When a non-empty system prompt is provided, it is converted to a <c>SystemPrompt</c> and stored on the agent.
        /// - Persists the updated agent configuration via <c>ConfigurationManager.SaveConfigurationAsync</c>.
        /// - Refreshes the UI header/display to reflect the new configuration.
        /// - Any exceptions are caught and reported to the user via an error dialog; the method does not propagate exceptions.
        /// </remarks>
        private async Task UpdateConfiguration()
        {
            try
            {
                var temperature = currentConfig.Temperature;
                if (currentConfig.Model.Contains("gpt-5", StringComparison.OrdinalIgnoreCase))
                {
                    temperature = 1.0;
                }
                
                agent.Configuration.Model = currentConfig.Model;
                agent.Configuration.Temperature = temperature;
                agent.Configuration.MaxTokens = currentConfig.MaxTokens;
                agent.Configuration.TopP = currentConfig.TopP;
                agent.Configuration.MaintainHistory = currentConfig.MaintainHistory;
                agent.Configuration.MaxHistoryMessages = currentConfig.MaxHistoryMessages;
                agent.Configuration.EnableTools = currentConfig.EnableTools;
                agent.Configuration.EnableStreaming = currentConfig.EnableStreaming;
                agent.Configuration.ToolNames = currentConfig.ToolNames ?? new List<string>();
                agent.Configuration.RequireCommandApproval = currentConfig.RequireCommandApproval;
                
                if (!string.IsNullOrWhiteSpace(currentConfig.SystemPrompt))
                {
                    agent.Configuration.SystemPrompt = await SystemPrompt.Create(currentConfig.SystemPrompt);
                }

                await ConfigurationManager.SaveConfigurationAsync(
                    ConfigurationManager.FromAgentConfiguration(agent.Configuration));

                UpdateConfigurationDisplay();
                Application.Refresh();
            }
            catch (Exception ex)
            {
                MessageBox.ErrorQuery("Configuration Error", $"Failed to update configuration: {ex.Message}", "OK");
            }
        }

        /// <summary>
        /// Refreshes the header section of the chat view to reflect the current agent configuration (name, model, streaming and tool settings).
        /// </summary>
        /// <remarks>
        /// Scans the existing chat content for the welcome header (starts with "Welcome to Saturn" and the following separator line)
        /// and replaces the configuration lines in that header block with values from <c>agent.Configuration</c>.
        /// The method preserves the rest of the chat content and restores the text view's cursor position and scroll (TopRow) after updating.
        /// Side effects: mutates <c>chatView.Text</c>, <c>chatView.CursorPosition</c>, and <c>chatView.TopRow</c>.
        /// </remarks>
        private void UpdateConfigurationDisplay()
        {
            var currentText = chatView.Text.ToString();
            var lines = currentText.Split('\n');
            var updatedLines = new List<string>();
            bool inHeader = false;
            bool skipConfigLines = false;
            int headerEndIndex = -1;
            
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                
                if (line.StartsWith("Welcome to Saturn"))
                {
                    inHeader = true;
                    updatedLines.Add(line);
                }
                else if (inHeader && line.StartsWith("================================"))
                {
                    if (!skipConfigLines)
                    {
                        updatedLines.Add(line);
                        updatedLines.Add($"Agent: {agent.Name}");
                        updatedLines.Add($"Model: {agent.Configuration.Model}");
                        updatedLines.Add($"Streaming: {(agent.Configuration.EnableStreaming ? "Enabled" : "Disabled")}");
                        updatedLines.Add($"Tools: {(agent.Configuration.EnableTools ? "Enabled" : "Disabled")}");
                        if (agent.Configuration.EnableTools && agent.Configuration.ToolNames != null && agent.Configuration.ToolNames.Count > 0)
                        {
                            updatedLines.Add($"Available Tools: {string.Join(", ", agent.Configuration.ToolNames)}");
                        }
                        skipConfigLines = true;
                    }
                    else
                    {
                        updatedLines.Add(line);
                        inHeader = false;
                        skipConfigLines = false;
                    }
                }
                else if (skipConfigLines && 
                    (line.StartsWith("Agent:") || line.StartsWith("Model:") || 
                     line.StartsWith("Streaming:") || line.StartsWith("Tools:") || 
                     line.StartsWith("Available Tools:")))
                {
                    continue;
                }
                else
                {
                    updatedLines.Add(line);
                }
            }
            
            var currentPosition = chatView.CursorPosition;
            var currentTopRow = chatView.TopRow;
            chatView.Text = string.Join("\n", updatedLines);
            chatView.CursorPosition = currentPosition;
            chatView.TopRow = currentTopRow;
        }

        /// <summary>
        /// Shows the Load Chat dialog and, if the user selects a session, loads that chat session into the UI.
        /// </summary>
        /// <returns>A task that completes after the dialog closes and any selected session has been loaded.</returns>
        private async Task ShowLoadChatDialog()
        {
            var dialog = new LoadChatDialog();
            Application.Run(dialog);
            
            if (!string.IsNullOrEmpty(dialog.SelectedSessionId))
            {
                await LoadChatSession(dialog.SelectedSessionId);
            }
        }

        private async Task LoadChatSession(string sessionId)
        {
            try
            {
                var repository = new ChatHistoryRepository();
                var session = await repository.GetSessionAsync(sessionId);
                
                if (session == null)
                {
                    MessageBox.ErrorQuery("Error", "Session not found", "OK");
                    return;
                }

                var messages = await repository.GetMessagesAsync(sessionId);
                var toolCalls = await repository.GetToolCallsAsync(sessionId);
                
                agent.ClearHistory();
                chatView.Text = "";
                toolCallsView.Text = "";
                
                agent.CurrentSessionId = sessionId;
                
                if (!string.IsNullOrEmpty(session.SystemPrompt))
                {
                    agent.ChatHistory.Add(new Message
                    {
                        Role = "system",
                        Content = JsonDocument.Parse(JsonSerializer.Serialize(session.SystemPrompt)).RootElement
                    });
                }
                
                var chatContent = new StringBuilder();
                chatContent.AppendLine($"=== Loaded Chat: {session.Title} ===");
                chatContent.AppendLine($"Model: {session.Model} | Created: {session.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm}");
                chatContent.AppendLine();
                
                foreach (var message in messages)
                {
                    var timestamp = message.Timestamp.ToLocalTime().ToString("HH:mm:ss");
                    
                    if (message.Role == "system")
                    {
                        chatContent.AppendLine($"[System Prompt]\n{message.Content}\n");
                        continue;
                    }
                    else if (message.Role == "user")
                    {
                        chatContent.AppendLine($"[{timestamp}] You:\n{message.Content}\n");
                    }
                    else if (message.Role == "assistant")
                    {
                        if (!string.IsNullOrEmpty(message.ToolCallsJson))
                        {
                            chatContent.AppendLine($"[{timestamp}] Assistant: [Making tool calls...]\n");
                        }
                        else if (message.Content != "null" && !string.IsNullOrEmpty(message.Content))
                        {
                            var renderedContent = markdownRenderer.RenderToTerminal(message.Content);
                            chatContent.AppendLine($"[{timestamp}] Assistant:\n{renderedContent}\n");
                        }
                    }
                    else if (message.Role == "tool")
                    {
                        var toolResult = message.Content.Length > 500 
                            ? message.Content.Substring(0, 500) + "...\n[Output truncated]" 
                            : message.Content;
                        chatContent.AppendLine($"[{timestamp}] Tool Result ({message.Name}):\n{toolResult}\n");
                    }
                    
                    Message openRouterMessage;
                    try
                    {
                        var jsonDoc = JsonDocument.Parse(message.Content);
                        openRouterMessage = new Message
                        {
                            Role = message.Role,
                            Content = jsonDoc.RootElement,
                            Name = message.Name,
                            ToolCallId = message.ToolCallId
                        };
                    }
                    catch
                    {
                        openRouterMessage = new Message
                        {
                            Role = message.Role,
                            Content = JsonDocument.Parse(JsonSerializer.Serialize(message.Content)).RootElement,
                            Name = message.Name,
                            ToolCallId = message.ToolCallId
                        };
                    }
                    
                    if (!string.IsNullOrEmpty(message.ToolCallsJson))
                    {
                        try
                        {
                            openRouterMessage.ToolCalls = JsonSerializer.Deserialize<ToolCallRequest[]>(message.ToolCallsJson);
                        }
                        catch { }
                    }
                    
                    agent.ChatHistory.Add(openRouterMessage);
                }
                
                if (toolCalls.Any())
                {
                    var toolCallsContent = new StringBuilder();
                    toolCallsContent.AppendLine("=== Tool Call History ===");
                    foreach (var toolCall in toolCalls)
                    {
                        var timestamp = toolCall.Timestamp.ToLocalTime().ToString("HH:mm:ss");
                        toolCallsContent.AppendLine($"[{timestamp}] {toolCall.ToolName}");
                        if (!string.IsNullOrEmpty(toolCall.AgentName))
                        {
                            toolCallsContent.AppendLine($"  Agent: {toolCall.AgentName}");
                        }
                        toolCallsContent.AppendLine($"  Duration: {toolCall.DurationMs}ms");
                        if (!string.IsNullOrEmpty(toolCall.Error))
                        {
                            toolCallsContent.AppendLine($"  Error: {toolCall.Error}");
                        }
                        toolCallsContent.AppendLine("───────────────");
                    }
                    toolCallsView.Text = toolCallsContent.ToString();
                }
                
                chatView.Text = chatContent.ToString();
                chatView.CursorPosition = new Point(0, chatView.Lines);
                
                if (session.ChatType == "agent" && !string.IsNullOrEmpty(session.ParentSessionId))
                {
                    UpdateAgentStatus($"Loaded agent session: {session.AgentName}");
                }
                else
                {
                    UpdateAgentStatus("Chat history loaded");
                }
                
                repository.Dispose();
                Application.Refresh();
            }
            catch (Exception ex)
            {
                MessageBox.ErrorQuery("Error", $"Failed to load chat: {ex.Message}", "OK");
            }
        }
    }
}
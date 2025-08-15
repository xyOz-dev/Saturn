using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Terminal.Gui;
using Saturn.Data;
using Saturn.Data.Models;

namespace Saturn.UI.Dialogs
{
    public class LoadChatDialog : Dialog
    {
        private ListView _sessionListView;
        private TextView _previewTextView;
        private ComboBox _filterComboBox;
        private Label _sessionInfoLabel;
        private ChatHistoryRepository _repository;
        private List<ChatSession> _sessions;
        private List<ChatSession> _filteredSessions;
        private string? _selectedSessionId;

        public string? SelectedSessionId => _selectedSessionId;

        public LoadChatDialog() : base("Load Chat History", 80, 24)
        {
            _repository = new ChatHistoryRepository();
            _sessions = new List<ChatSession>();
            _filteredSessions = new List<ChatSession>();
            
            InitializeComponents();
            _ = LoadSessionsAsync();
        }

        private void InitializeComponents()
        {
            var filterLabel = new Label("Filter by type:")
            {
                X = 1,
                Y = 1
            };
            Add(filterLabel);

            _filterComboBox = new ComboBox()
            {
                X = Pos.Right(filterLabel) + 1,
                Y = 1,
                Width = 20,
                Height = 5
            };
            _filterComboBox.SetSource(new[] { "All", "Main", "Agent" });
            _filterComboBox.SelectedItem = 0;
            _filterComboBox.SelectedItemChanged += (args) => FilterSessions();
            Add(_filterComboBox);

            var sessionsLabel = new Label("Sessions:")
            {
                X = 1,
                Y = 3
            };
            Add(sessionsLabel);

            _sessionListView = new ListView()
            {
                X = 1,
                Y = 4,
                Width = Dim.Percent(40),
                Height = Dim.Fill(5)
            };
            _sessionListView.SelectedItemChanged += OnSessionSelected;
            _sessionListView.OpenSelectedItem += (args) =>
            {
                if (_filteredSessions != null && 
                    _sessionListView.SelectedItem >= 0 && 
                    _sessionListView.SelectedItem < _filteredSessions.Count)
                {
                    _selectedSessionId = _filteredSessions[_sessionListView.SelectedItem].Id;
                    Application.RequestStop();
                }
            };
            Add(_sessionListView);

            var previewLabel = new Label("Preview:")
            {
                X = Pos.Right(_sessionListView) + 2,
                Y = 3
            };
            Add(previewLabel);

            _previewTextView = new TextView()
            {
                X = Pos.Right(_sessionListView) + 2,
                Y = 4,
                Width = Dim.Fill(1),
                Height = Dim.Fill(7),
                ReadOnly = true
            };
            Add(_previewTextView);

            _sessionInfoLabel = new Label("")
            {
                X = Pos.Right(_sessionListView) + 2,
                Y = Pos.Bottom(_previewTextView) + 1,
                Width = Dim.Fill(1)
            };
            Add(_sessionInfoLabel);

            var loadButton = new Button("_Load")
            {
                X = Pos.Center() - 10,
                Y = Pos.AnchorEnd(3),
                IsDefault = true
            };
            loadButton.Clicked += () =>
            {
                if (_filteredSessions != null && 
                    _sessionListView.SelectedItem >= 0 && 
                    _sessionListView.SelectedItem < _filteredSessions.Count)
                {
                    _selectedSessionId = _filteredSessions[_sessionListView.SelectedItem].Id;
                    Application.RequestStop();
                }
            };
            Add(loadButton);

            var cancelButton = new Button("_Cancel")
            {
                X = Pos.Center() + 2,
                Y = Pos.AnchorEnd(3)
            };
            cancelButton.Clicked += () =>
            {
                _selectedSessionId = null;
                Application.RequestStop();
            };
            Add(cancelButton);
        }

        private async Task LoadSessionsAsync()
        {
            try
            {
                _sessions = await _repository.GetSessionsAsync(limit: 100);
                FilterSessions();
            }
            catch (Exception ex)
            {
                MessageBox.ErrorQuery("Error", $"Failed to load sessions: {ex.Message}", "OK");
            }
        }

        private void FilterSessions()
        {
            if (_filterComboBox == null || _sessions == null)
                return;
                
            var filterType = _filterComboBox.Text?.ToString() ?? "All";
            
            _filteredSessions = filterType switch
            {
                "Main" => _sessions.Where(s => s.ChatType == "main").ToList(),
                "Agent" => _sessions.Where(s => s.ChatType == "agent").ToList(),
                _ => _sessions.ToList()
            };

            var sessionNames = _filteredSessions.Select(s =>
            {
                var typeIndicator = s.ChatType == "agent" ? "[A] " : "";
                var timestamp = s.UpdatedAt.ToLocalTime().ToString("MM/dd HH:mm");
                return $"{typeIndicator}{s.Title} ({timestamp})";
            }).ToArray();

            _sessionListView.SetSource(sessionNames);
            
            if (_filteredSessions.Count > 0)
            {
                _sessionListView.SelectedItem = 0;
                OnSessionSelected(null);
            }
            else
            {
                _previewTextView.Text = "";
                _sessionInfoLabel.Text = "";
            }
        }

        private async void OnSessionSelected(ListViewItemEventArgs? args)
        {
            if (_sessionListView.SelectedItem < 0 || _sessionListView.SelectedItem >= _filteredSessions.Count)
                return;

            var session = _filteredSessions[_sessionListView.SelectedItem];
            
            _sessionInfoLabel.Text = $"Model: {session.Model ?? "N/A"} | " +
                                    $"Agent: {session.AgentName ?? "Main"} | " +
                                    $"Messages: Loading...";

            try
            {
                var messages = await _repository.GetMessagesAsync(session.Id);
                var preview = GeneratePreview(messages, session);
                _previewTextView.Text = preview;
                
                var toolCalls = await _repository.GetToolCallsAsync(session.Id);
                _sessionInfoLabel.Text = $"Model: {session.Model ?? "N/A"} | " +
                                       $"Agent: {session.AgentName ?? "Main"} | " +
                                       $"Messages: {messages.Count} | " +
                                       $"Tool Calls: {toolCalls.Count}";
            }
            catch (Exception ex)
            {
                _previewTextView.Text = $"Error loading preview: {ex.Message}";
            }
        }

        private string GeneratePreview(List<ChatMessage> messages, ChatSession session)
        {
            var preview = new System.Text.StringBuilder();
            
            if (!string.IsNullOrEmpty(session.SystemPrompt))
            {
                preview.AppendLine("[System Prompt]");
                preview.AppendLine(TruncateText(session.SystemPrompt, 200));
                preview.AppendLine();
            }

            int messageCount = Math.Min(messages.Count, 10);
            for (int i = 0; i < messageCount; i++)
            {
                var msg = messages[i];
                var role = msg.Role.ToUpper();
                var content = TruncateText(msg.Content, 150);
                
                if (!string.IsNullOrEmpty(msg.ToolCallsJson))
                {
                    preview.AppendLine($"[{role}] [Tool Calls]");
                }
                else if (msg.Role == "tool")
                {
                    preview.AppendLine($"[TOOL: {msg.Name}]");
                    preview.AppendLine(content);
                }
                else
                {
                    preview.AppendLine($"[{role}]");
                    preview.AppendLine(content);
                }
                preview.AppendLine();
            }

            if (messages.Count > 10)
            {
                preview.AppendLine($"... and {messages.Count - 10} more messages");
            }

            return preview.ToString();
        }

        private string TruncateText(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text))
                return "";
                
            if (text.Length <= maxLength)
                return text;
                
            return text.Substring(0, maxLength - 3) + "...";
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _repository?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
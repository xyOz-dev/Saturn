using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Terminal.Gui;

namespace Saturn.UI.Dialogs
{
    public class WorkspaceSwitchDialog : Dialog
    {
        private ListView _recentListView = null!;
        private TextField _pathField = null!;
        private readonly List<string> _recentWorkspaces;

        public string? SelectedPath { get; private set; }

        public WorkspaceSwitchDialog(IEnumerable<string> recentWorkspaces, string currentWorkspace)
            : base("Switch Workspace", 70, 20)
        {
            _recentWorkspaces = recentWorkspaces
                .Where(p => !string.Equals(p, currentWorkspace, StringComparison.OrdinalIgnoreCase))
                .Where(Directory.Exists)
                .ToList();

            InitializeComponents(currentWorkspace);
        }

        private void InitializeComponents(string currentWorkspace)
        {
            var currentLabel = new Label($"Current: {currentWorkspace}")
            {
                X = 1,
                Y = 1,
                Width = Dim.Fill(1)
            };
            Add(currentLabel);

            var recentLabel = new Label("Recent workspaces (Enter to pick):")
            {
                X = 1,
                Y = 3
            };
            Add(recentLabel);

            _recentListView = new ListView()
            {
                X = 1,
                Y = 4,
                Width = Dim.Fill(1),
                Height = Dim.Fill(8)
            };
            _recentListView.SetSource(_recentWorkspaces);
            _recentListView.SelectedItemChanged += args =>
            {
                if (args.Item >= 0 && args.Item < _recentWorkspaces.Count)
                {
                    _pathField.Text = _recentWorkspaces[args.Item];
                }
            };
            _recentListView.OpenSelectedItem += args =>
            {
                if (args.Item >= 0 && args.Item < _recentWorkspaces.Count)
                {
                    SelectedPath = _recentWorkspaces[args.Item];
                    Application.RequestStop();
                }
            };
            Add(_recentListView);

            var pathLabel = new Label("Directory path:")
            {
                X = 1,
                Y = Pos.AnchorEnd(6)
            };
            Add(pathLabel);

            _pathField = new TextField("")
            {
                X = 1,
                Y = Pos.AnchorEnd(5),
                Width = Dim.Fill(1)
            };
            if (_recentWorkspaces.Count > 0)
            {
                _pathField.Text = _recentWorkspaces[0];
            }
            Add(_pathField);

            var switchButton = new Button("_Switch")
            {
                X = Pos.Center() - 10,
                Y = Pos.AnchorEnd(3),
                IsDefault = true
            };
            switchButton.Clicked += () =>
            {
                var path = _pathField.Text?.ToString()?.Trim();
                if (string.IsNullOrWhiteSpace(path))
                {
                    MessageBox.ErrorQuery("Switch Workspace", "Enter a directory path.", "OK");
                    return;
                }
                SelectedPath = path;
                Application.RequestStop();
            };
            Add(switchButton);

            var cancelButton = new Button("_Cancel")
            {
                X = Pos.Center() + 2,
                Y = Pos.AnchorEnd(3)
            };
            cancelButton.Clicked += () =>
            {
                SelectedPath = null;
                Application.RequestStop();
            };
            Add(cancelButton);
        }
    }
}

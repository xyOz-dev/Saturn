using System;
using System.Collections.Generic;
using System.Linq;
using Terminal.Gui;
using Saturn.Tools.Core;

namespace Saturn.UI.Dialogs
{
    public class ToolSelectionDialog : Dialog
    {
        private ListView toolListView = null!;
        private Label descriptionLabel = null!;
        private Button selectAllButton = null!;
        private Button clearAllButton = null!;
        private Button okButton = null!;
        private Button cancelButton = null!;
        
        private List<ITool> availableTools = null!;
        private HashSet<string> selectedToolNames = null!;
        private string[] toolDisplayNames = null!;
        
        public List<string> SelectedTools => selectedToolNames.ToList();
        
        public ToolSelectionDialog(List<string>? currentlySelectedTools = null)
            : base("Select Tools", 70, 20)
        {
            ColorScheme = Colors.Dialog;
            selectedToolNames = new HashSet<string>(currentlySelectedTools ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            
            LoadAvailableTools();
            InitializeComponents();
            UpdateTitle();
        }
        
        private void LoadAvailableTools()
        {
            availableTools = ToolRegistry.Instance.GetAll().OrderBy(t => t.Name).ToList();
            toolDisplayNames = new string[availableTools.Count];
            UpdateToolDisplayNames();
        }
        
        private void UpdateToolDisplayNames()
        {
            for (int i = 0; i < availableTools.Count; i++)
            {
                var tool = availableTools[i];
                var isSelected = selectedToolNames.Contains(tool.Name);
                var checkbox = isSelected ? "[✓]" : "[ ]";
                var truncatedDesc = tool.Description.Length > 40 
                    ? tool.Description.Substring(0, 37) + "..." 
                    : tool.Description;
                toolDisplayNames[i] = $"{checkbox} {tool.Name,-20} - {truncatedDesc}";
            }
        }
        
        private void InitializeComponents()
        {
            toolListView = new ListView(toolDisplayNames)
            {
                X = 1,
                Y = 1,
                Width = Dim.Fill(1),
                Height = Dim.Fill(5),
                CanFocus = true
            };
            
            toolListView.SelectedItemChanged += OnSelectedItemChanged;
            toolListView.KeyPress += OnListViewKeyPress;
            
            var separatorLine = new Label(new string('─', 68))
            {
                X = 0,
                Y = Pos.Bottom(toolListView) + 1,
                Width = Dim.Fill()
            };
            
            descriptionLabel = new Label("Select a tool to see its full description")
            {
                X = 1,
                Y = Pos.Bottom(separatorLine),
                Width = Dim.Fill(1),
                Height = 2,
                TextAlignment = TextAlignment.Left
            };
            
            var buttonSeparator = new Label(new string('─', 68))
            {
                X = 0,
                Y = Pos.Bottom(descriptionLabel) + 1,
                Width = Dim.Fill()
            };
            
            selectAllButton = new Button("Select _All")
            {
                X = Pos.Center() - 25,
                Y = Pos.Bottom(buttonSeparator) + 1
            };
            selectAllButton.Clicked += OnSelectAllClicked;
            
            clearAllButton = new Button("_Clear All")
            {
                X = Pos.Right(selectAllButton) + 2,
                Y = Pos.Top(selectAllButton)
            };
            clearAllButton.Clicked += OnClearAllClicked;
            
            okButton = new Button("_OK", true)
            {
                X = Pos.Right(clearAllButton) + 4,
                Y = Pos.Top(selectAllButton)
            };
            okButton.Clicked += () => Application.RequestStop();
            
            cancelButton = new Button("_Cancel")
            {
                X = Pos.Right(okButton) + 2,
                Y = Pos.Top(selectAllButton)
            };
            cancelButton.Clicked += () => 
            {
                selectedToolNames.Clear();
                Application.RequestStop();
            };
            
            Add(toolListView, separatorLine, descriptionLabel, buttonSeparator, 
                selectAllButton, clearAllButton, okButton, cancelButton);
            
            if (availableTools.Count > 0)
            {
                OnSelectedItemChanged(new ListViewItemEventArgs(0, null));
            }
            
            toolListView.SetFocus();
        }
        
        private void OnSelectedItemChanged(ListViewItemEventArgs args)
        {
            if (args.Item >= 0 && args.Item < availableTools.Count)
            {
                var tool = availableTools[args.Item];
                descriptionLabel.Text = $"Description: {tool.Description}";
            }
        }
        
        private void OnListViewKeyPress(KeyEventEventArgs args)
        {
            if (args.KeyEvent.Key == Key.Space || args.KeyEvent.Key == Key.Enter)
            {
                ToggleSelectedItem();
                args.Handled = true;
            }
            else if (args.KeyEvent.Key == (Key.CtrlMask | Key.A))
            {
                SelectAll();
                args.Handled = true;
            }
            else if (args.KeyEvent.Key == (Key.CtrlMask | Key.D))
            {
                ClearAll();
                args.Handled = true;
            }
        }
        
        private void ToggleSelectedItem()
        {
            var selectedIndex = toolListView.SelectedItem;
            if (selectedIndex >= 0 && selectedIndex < availableTools.Count)
            {
                var tool = availableTools[selectedIndex];
                if (selectedToolNames.Contains(tool.Name))
                {
                    selectedToolNames.Remove(tool.Name);
                }
                else
                {
                    selectedToolNames.Add(tool.Name);
                }
                
                UpdateToolDisplayNames();
                toolListView.SetSource(toolDisplayNames);
                toolListView.SelectedItem = selectedIndex;
                UpdateTitle();
            }
        }
        
        private void OnSelectAllClicked()
        {
            SelectAll();
        }
        
        private void SelectAll()
        {
            foreach (var tool in availableTools)
            {
                selectedToolNames.Add(tool.Name);
            }
            UpdateToolDisplayNames();
            toolListView.SetSource(toolDisplayNames);
            UpdateTitle();
        }
        
        private void OnClearAllClicked()
        {
            ClearAll();
        }
        
        private void ClearAll()
        {
            selectedToolNames.Clear();
            UpdateToolDisplayNames();
            toolListView.SetSource(toolDisplayNames);
            UpdateTitle();
        }
        
        private void UpdateTitle()
        {
            Title = $"Select Tools ({selectedToolNames.Count} of {availableTools.Count} selected)";
        }
    }
}
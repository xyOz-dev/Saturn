using System;
using System.Collections.Generic;
using System.Linq;
using Terminal.Gui;
using Saturn.Agents.Core;

namespace Saturn.UI.Dialogs
{
    public class ModeSelectionDialog : Dialog
    {
        private ListView modeListView = null!;
        private Label descriptionLabel = null!;
        private Label detailsLabel = null!;
        private Button selectButton = null!;
        private Button newButton = null!;
        private Button editButton = null!;
        private Button duplicateButton = null!;
        private Button deleteButton = null!;
        private Button importButton = null!;
        private Button exportButton = null!;
        private Button cancelButton = null!;
        
        private List<Mode> modes = null!;
        private string[] modeDisplayNames = null!;
        
        public Mode? SelectedMode { get; private set; }
        public bool ShouldCreateNew { get; private set; }
        public Mode? ModeToEdit { get; private set; }
        
        public ModeSelectionDialog()
            : base("Select Mode", 90, 26)
        {
            ColorScheme = Colors.Dialog;
            LoadModes();
            InitializeComponents();
        }
        
        private void LoadModes()
        {
            modes = ModeManager.Instance.GetAllModes().ToList();
            modeDisplayNames = new string[modes.Count];
            UpdateModeDisplayNames();
        }
        
        private void UpdateModeDisplayNames()
        {
            for (int i = 0; i < modes.Count; i++)
            {
                var mode = modes[i];
                var isDefault = mode.IsDefault ? " [DEFAULT]" : "";
                var toolCount = mode.ToolNames?.Count ?? 0;
                modeDisplayNames[i] = $"{mode.Name,-25} | {mode.AgentName,-15} | {toolCount} tools{isDefault}";
            }
        }
        
        private void InitializeComponents()
        {
            var listLabel = new Label("Available Modes:")
            {
                X = 1,
                Y = 1
            };
            
            modeListView = new ListView(modeDisplayNames)
            {
                X = 1,
                Y = 2,
                Width = Dim.Fill(1),
                Height = 8,
                CanFocus = true
            };
            
            modeListView.SelectedItemChanged += OnSelectedModeChanged;
            modeListView.KeyPress += OnListViewKeyPress;
            
            var separatorLine = new Label(new string('─', 88))
            {
                X = 0,
                Y = Pos.Bottom(modeListView) + 1,
                Width = Dim.Fill()
            };
            
            descriptionLabel = new Label("Description: ")
            {
                X = 1,
                Y = Pos.Bottom(separatorLine),
                Width = Dim.Fill(1),
                Height = 1
            };
            
            detailsLabel = new Label("")
            {
                X = 1,
                Y = Pos.Bottom(descriptionLabel),
                Width = Dim.Fill(1),
                Height = 3,
                TextAlignment = TextAlignment.Left
            };
            
            var buttonSeparator = new Label(new string('─', 88))
            {
                X = 0,
                Y = Pos.Bottom(detailsLabel) + 1,
                Width = Dim.Fill()
            };
            
            selectButton = new Button("_Select", true)
            {
                X = Pos.Center() - 20,
                Y = Pos.Bottom(buttonSeparator) + 1
            };
            selectButton.Clicked += () => OnSelectClicked();
            
            newButton = new Button("_New")
            {
                X = Pos.Right(selectButton) + 2,
                Y = Pos.Top(selectButton)
            };
            newButton.Clicked += () => OnNewClicked();
            
            editButton = new Button("_Edit")
            {
                X = Pos.Right(newButton) + 2,
                Y = Pos.Top(selectButton)
            };
            editButton.Clicked += () => OnEditClicked();
            
            duplicateButton = new Button("_Duplicate")
            {
                X = Pos.Right(editButton) + 2,
                Y = Pos.Top(selectButton)
            };
            duplicateButton.Clicked += () => OnDuplicateClicked();
            
            deleteButton = new Button("De_lete")
            {
                X = Pos.Center() - 20,
                Y = Pos.Bottom(selectButton) + 1
            };
            deleteButton.Clicked += () => OnDeleteClicked();
            
            importButton = new Button("_Import")
            {
                X = Pos.Right(deleteButton) + 2,
                Y = Pos.Top(deleteButton)
            };
            importButton.Clicked += () => OnImportClicked();
            
            exportButton = new Button("E_xport")
            {
                X = Pos.Right(importButton) + 2,
                Y = Pos.Top(deleteButton)
            };
            exportButton.Clicked += () => OnExportClicked();
            
            cancelButton = new Button("_Cancel")
            {
                X = Pos.Right(exportButton) + 2,
                Y = Pos.Top(deleteButton)
            };
            cancelButton.Clicked += () => Application.RequestStop();
            
            Add(listLabel, modeListView, separatorLine, descriptionLabel, detailsLabel,
                buttonSeparator, selectButton, newButton, editButton, duplicateButton,
                deleteButton, importButton, exportButton, cancelButton);
            
            if (modes.Count > 0)
            {
                modeListView.SelectedItem = 0;
                OnSelectedModeChanged(new ListViewItemEventArgs(0, null));
            }
            
            UpdateButtonStates();
            modeListView.SetFocus();
        }
        
        private void OnSelectedModeChanged(ListViewItemEventArgs args)
        {
            if (args.Item >= 0 && args.Item < modes.Count)
            {
                var mode = modes[args.Item];
                descriptionLabel.Text = $"Description: {mode.Description ?? "No description"}";
                
                var toolsText = mode.ToolNames?.Count > 0 
                    ? string.Join(", ", mode.ToolNames.Take(10)) + (mode.ToolNames.Count > 10 ? "..." : "")
                    : "No tools selected";
                    
                var promptInfo = string.IsNullOrWhiteSpace(mode.SystemPromptOverride) 
                    ? "Default system prompt" 
                    : "Custom system prompt";
                    
                detailsLabel.Text = $"Model: {mode.Model} | Temperature: {mode.Temperature:F2}\n" +
                                   $"Tools: {toolsText}\n" +
                                   $"Prompt: {promptInfo}";
                
                UpdateButtonStates();
            }
        }
        
        private void UpdateButtonStates()
        {
            var selectedIndex = modeListView.SelectedItem;
            if (selectedIndex >= 0 && selectedIndex < modes.Count)
            {
                var mode = modes[selectedIndex];
                editButton.Enabled = !mode.IsDefault;
                deleteButton.Enabled = !mode.IsDefault;
                exportButton.Enabled = !mode.IsDefault;
            }
        }
        
        private void OnListViewKeyPress(KeyEventEventArgs args)
        {
            if (args.KeyEvent.Key == Key.Enter)
            {
                OnSelectClicked();
                args.Handled = true;
            }
            else if (args.KeyEvent.Key == Key.DeleteChar)
            {
                OnDeleteClicked();
                args.Handled = true;
            }
        }
        
        private void OnSelectClicked()
        {
            var selectedIndex = modeListView.SelectedItem;
            if (selectedIndex >= 0 && selectedIndex < modes.Count)
            {
                SelectedMode = modes[selectedIndex];
                Application.RequestStop();
            }
        }
        
        private void OnNewClicked()
        {
            ShouldCreateNew = true;
            Application.RequestStop();
        }
        
        private void OnEditClicked()
        {
            var selectedIndex = modeListView.SelectedItem;
            if (selectedIndex >= 0 && selectedIndex < modes.Count)
            {
                var mode = modes[selectedIndex];
                if (!mode.IsDefault)
                {
                    ModeToEdit = mode;
                    Application.RequestStop();
                }
            }
        }
        
        private async void OnDuplicateClicked()
        {
            var selectedIndex = modeListView.SelectedItem;
            if (selectedIndex >= 0 && selectedIndex < modes.Count)
            {
                try
                {
                    var mode = modes[selectedIndex];
                    var duplicated = await ModeManager.Instance.DuplicateModeAsync(mode.Id);
                    
                    MessageBox.Query("Success", $"Mode '{duplicated.Name}' created successfully", "OK");
                    
                    LoadModes();
                    modeListView.SetSource(modeDisplayNames);
                    
                    var newIndex = modes.FindIndex(m => m.Id == duplicated.Id);
                    if (newIndex >= 0)
                    {
                        modeListView.SelectedItem = newIndex;
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.ErrorQuery("Error", $"Failed to duplicate mode: {ex.Message}", "OK");
                }
            }
        }
        
        private async void OnDeleteClicked()
        {
            var selectedIndex = modeListView.SelectedItem;
            if (selectedIndex >= 0 && selectedIndex < modes.Count)
            {
                var mode = modes[selectedIndex];
                if (mode.IsDefault)
                {
                    MessageBox.ErrorQuery("Error", "Cannot delete the default mode", "OK");
                    return;
                }
                
                var result = MessageBox.Query("Confirm Delete", 
                    $"Are you sure you want to delete the mode '{mode.Name}'?", 
                    "Yes", "No");
                    
                if (result == 0)
                {
                    try
                    {
                        await ModeManager.Instance.DeleteModeAsync(mode.Id);
                        
                        LoadModes();
                        modeListView.SetSource(modeDisplayNames);
                        
                        if (modeListView.SelectedItem >= modes.Count)
                        {
                            modeListView.SelectedItem = modes.Count - 1;
                        }
                        
                        if (modes.Count > 0)
                        {
                            OnSelectedModeChanged(new ListViewItemEventArgs(modeListView.SelectedItem, null));
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.ErrorQuery("Error", $"Failed to delete mode: {ex.Message}", "OK");
                    }
                }
            }
        }
        
        private async void OnImportClicked()
        {
            var openDialog = new OpenDialog("Import Mode", "Select a mode file to import")
            {
                AllowsMultipleSelection = false,
                CanChooseFiles = true,
                CanChooseDirectories = false
            };
            
            openDialog.AllowedFileTypes = new[] { ".json" };
            
            Application.Run(openDialog);
            
            if (!openDialog.Canceled && openDialog.FilePaths.Count > 0)
            {
                try
                {
                    var imported = await ModeManager.Instance.ImportModeAsync(openDialog.FilePaths[0]);
                    MessageBox.Query("Success", $"Mode '{imported.Name}' imported successfully", "OK");
                    
                    LoadModes();
                    modeListView.SetSource(modeDisplayNames);
                    
                    var newIndex = modes.FindIndex(m => m.Id == imported.Id);
                    if (newIndex >= 0)
                    {
                        modeListView.SelectedItem = newIndex;
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.ErrorQuery("Error", $"Failed to import mode: {ex.Message}", "OK");
                }
            }
        }
        
        private async void OnExportClicked()
        {
            var selectedIndex = modeListView.SelectedItem;
            if (selectedIndex >= 0 && selectedIndex < modes.Count)
            {
                var mode = modes[selectedIndex];
                if (mode.IsDefault)
                {
                    MessageBox.ErrorQuery("Error", "Cannot export the default mode", "OK");
                    return;
                }
                
                var saveDialog = new SaveDialog("Export Mode", "Save mode file")
                {
                    AllowedFileTypes = new[] { ".json" }
                };
                
                saveDialog.FilePath = $"{mode.Name.Replace(" ", "_")}.json";
                
                Application.Run(saveDialog);
                
                if (!saveDialog.Canceled && !string.IsNullOrWhiteSpace(saveDialog.FilePath.ToString()))
                {
                    try
                    {
                        await ModeManager.Instance.ExportModeAsync(mode.Id, saveDialog.FilePath.ToString());
                        MessageBox.Query("Success", $"Mode exported to {saveDialog.FilePath}", "OK");
                    }
                    catch (Exception ex)
                    {
                        MessageBox.ErrorQuery("Error", $"Failed to export mode: {ex.Message}", "OK");
                    }
                }
            }
        }
    }
}
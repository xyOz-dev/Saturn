using System;
using System.Collections.Generic;
using System.Linq;
using Terminal.Gui;
using Saturn.Skills;

namespace Saturn.UI.Dialogs
{
    public class SkillSelectionDialog : Dialog
    {
        private ListView skillListView = null!;
        private Label descriptionLabel = null!;
        private Label detailsLabel = null!;
        private CheckBox enableSkillsCheckBox = null!;
        private Button newButton = null!;
        private Button editButton = null!;
        private Button duplicateButton = null!;
        private Button deleteButton = null!;
        private Button closeButton = null!;

        private List<Skill> skills = null!;
        private string[] skillDisplayNames = null!;

        public bool ShouldCreateNew { get; private set; }
        public Skill? SkillToEdit { get; private set; }
        public bool SkillsEnabled { get; private set; }
        public bool SkillsEnabledChanged { get; private set; }

        private readonly bool initialSkillsEnabled;

        public SkillSelectionDialog(bool skillsEnabled)
            : base("Skills", 90, 26)
        {
            ColorScheme = Colors.Dialog;
            initialSkillsEnabled = skillsEnabled;
            SkillsEnabled = skillsEnabled;
            LoadSkills();
            InitializeComponents();
        }

        private void LoadSkills()
        {
            skills = SkillManager.GetAllSkills().ToList();
            skillDisplayNames = new string[skills.Count];
            UpdateSkillDisplayNames();
        }

        private void UpdateSkillDisplayNames()
        {
            for (int i = 0; i < skills.Count; i++)
            {
                var skill = skills[i];
                var enabled = skill.Enabled ? "" : " [DISABLED]";
                var scope = skill.Scope == SkillScope.Workspace ? "workspace" : "global";
                var targets = DescribeTargets(skill);
                skillDisplayNames[i] = $"{skill.Name,-30} | {scope,-9} | {targets}{enabled}";
            }
        }

        private static string DescribeTargets(Skill skill)
        {
            if (skill.ApplyToOrchestrator && skill.ApplyToSubAgents) return "orchestrator + sub-agents";
            if (skill.ApplyToOrchestrator) return "orchestrator";
            if (skill.ApplyToSubAgents) return "sub-agents";
            return "no targets";
        }

        private void InitializeComponents()
        {
            enableSkillsCheckBox = new CheckBox("Enable skill injection for this agent")
            {
                X = 1,
                Y = 1,
                Checked = SkillsEnabled
            };
            enableSkillsCheckBox.Toggled += (previous) =>
            {
                SkillsEnabled = enableSkillsCheckBox.Checked;
                SkillsEnabledChanged = SkillsEnabled != initialSkillsEnabled;
            };

            var listLabel = new Label("Available Skills:")
            {
                X = 1,
                Y = 3
            };

            skillListView = new ListView(skillDisplayNames)
            {
                X = 1,
                Y = 4,
                Width = Dim.Fill(1),
                Height = 8,
                CanFocus = true
            };

            skillListView.SelectedItemChanged += OnSelectedSkillChanged;
            skillListView.KeyPress += OnListViewKeyPress;

            var separatorLine = new Label(new string('─', 88))
            {
                X = 0,
                Y = Pos.Bottom(skillListView) + 1,
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

            newButton = new Button("_New", true)
            {
                X = Pos.Center() - 25,
                Y = Pos.Bottom(buttonSeparator) + 1
            };
            newButton.Clicked += () => OnNewClicked();

            editButton = new Button("_Edit")
            {
                X = Pos.Right(newButton) + 2,
                Y = Pos.Top(newButton)
            };
            editButton.Clicked += () => OnEditClicked();

            duplicateButton = new Button("_Duplicate")
            {
                X = Pos.Right(editButton) + 2,
                Y = Pos.Top(newButton)
            };
            duplicateButton.Clicked += () => OnDuplicateClicked();

            deleteButton = new Button("De_lete")
            {
                X = Pos.Right(duplicateButton) + 2,
                Y = Pos.Top(newButton)
            };
            deleteButton.Clicked += () => OnDeleteClicked();

            closeButton = new Button("_Close")
            {
                X = Pos.Right(deleteButton) + 2,
                Y = Pos.Top(newButton)
            };
            closeButton.Clicked += () => Application.RequestStop();

            Add(enableSkillsCheckBox, listLabel, skillListView, separatorLine,
                descriptionLabel, detailsLabel, buttonSeparator,
                newButton, editButton, duplicateButton, deleteButton, closeButton);

            if (skills.Count > 0)
            {
                skillListView.SelectedItem = 0;
                OnSelectedSkillChanged(new ListViewItemEventArgs(0, null));
            }
            else
            {
                descriptionLabel.Text = "No skills defined yet. Create one with New.";
            }

            skillListView.SetFocus();
        }

        private void OnSelectedSkillChanged(ListViewItemEventArgs args)
        {
            if (args.Item >= 0 && args.Item < skills.Count)
            {
                var skill = skills[args.Item];
                descriptionLabel.Text = $"Description: {(string.IsNullOrWhiteSpace(skill.Description) ? "No description" : skill.Description)}";

                var triggers = skill.Triggers.Count > 0
                    ? string.Join(", ", skill.Triggers.Take(8)) + (skill.Triggers.Count > 8 ? "..." : "")
                    : "None (loads on request via load_skill only)";

                var types = skill.SubAgentTypes == null || skill.SubAgentTypes.Count == 0
                    ? "all types"
                    : string.Join(", ", skill.SubAgentTypes);

                detailsLabel.Text = $"Triggers: {triggers}\n" +
                                    $"Sub-agent types: {types}\n" +
                                    $"Content: {skill.Content.Length} characters";
            }
        }

        private void OnListViewKeyPress(KeyEventEventArgs args)
        {
            if (args.KeyEvent.Key == Key.Enter)
            {
                OnEditClicked();
                args.Handled = true;
            }
            else if (args.KeyEvent.Key == Key.DeleteChar)
            {
                OnDeleteClicked();
                args.Handled = true;
            }
        }

        private void OnNewClicked()
        {
            ShouldCreateNew = true;
            Application.RequestStop();
        }

        private void OnEditClicked()
        {
            var selectedIndex = skillListView.SelectedItem;
            if (selectedIndex >= 0 && selectedIndex < skills.Count)
            {
                SkillToEdit = skills[selectedIndex];
                Application.RequestStop();
            }
        }

        private async void OnDuplicateClicked()
        {
            var selectedIndex = skillListView.SelectedItem;
            if (selectedIndex >= 0 && selectedIndex < skills.Count)
            {
                try
                {
                    var skill = skills[selectedIndex];
                    var duplicated = await SkillManager.DuplicateSkillAsync(skill.Id);

                    MessageBox.Query("Success", $"Skill '{duplicated.Name}' created successfully", "OK");

                    LoadSkills();
                    skillListView.SetSource(skillDisplayNames);

                    var newIndex = skills.FindIndex(s => s.Id == duplicated.Id);
                    if (newIndex >= 0)
                    {
                        skillListView.SelectedItem = newIndex;
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.ErrorQuery("Error", $"Failed to duplicate skill: {ex.Message}", "OK");
                }
            }
        }

        private async void OnDeleteClicked()
        {
            var selectedIndex = skillListView.SelectedItem;
            if (selectedIndex >= 0 && selectedIndex < skills.Count)
            {
                var skill = skills[selectedIndex];

                var result = MessageBox.Query("Confirm Delete",
                    $"Are you sure you want to delete the skill '{skill.Name}'?",
                    "Yes", "No");

                if (result == 0)
                {
                    try
                    {
                        await SkillManager.DeleteSkillAsync(skill.Id);

                        LoadSkills();
                        skillListView.SetSource(skillDisplayNames);

                        if (skillListView.SelectedItem >= skills.Count)
                        {
                            skillListView.SelectedItem = Math.Max(0, skills.Count - 1);
                        }

                        if (skills.Count > 0)
                        {
                            OnSelectedSkillChanged(new ListViewItemEventArgs(skillListView.SelectedItem, null));
                        }
                        else
                        {
                            descriptionLabel.Text = "No skills defined yet. Create one with New.";
                            detailsLabel.Text = "";
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.ErrorQuery("Error", $"Failed to delete skill: {ex.Message}", "OK");
                    }
                }
            }
        }
    }
}

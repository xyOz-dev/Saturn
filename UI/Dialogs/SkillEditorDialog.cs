using System;
using System.Collections.Generic;
using System.Linq;
using Terminal.Gui;
using Saturn.Agents.MultiAgent;
using Saturn.Skills;

namespace Saturn.UI.Dialogs
{
    public class SkillEditorDialog : Dialog
    {
        private TextField nameField = null!;
        private TextField descriptionField = null!;
        private TextField triggersField = null!;
        private RadioGroup scopeRadioGroup = null!;
        private CheckBox enabledCheckBox = null!;
        private CheckBox orchestratorCheckBox = null!;
        private CheckBox subAgentsCheckBox = null!;
        private List<CheckBox> agentTypeCheckBoxes = null!;
        private TextView contentTextView = null!;
        private Button saveButton = null!;
        private Button cancelButton = null!;

        private readonly Skill skill;
        private readonly bool isEditMode;

        public Skill? ResultSkill { get; private set; }

        public SkillEditorDialog(Skill? existingSkill = null)
            : base(existingSkill != null ? $"Edit Skill: {existingSkill.Name}" : "Create New Skill", 90, 30)
        {
            ColorScheme = Colors.Dialog;

            isEditMode = existingSkill != null;
            skill = existingSkill?.Clone() ?? new Skill();

            InitializeComponents();
        }

        private void InitializeComponents()
        {
            var nameLabel = new Label("Name:")
            {
                X = 1,
                Y = 1
            };

            nameField = new TextField(skill.Name)
            {
                X = Pos.Right(nameLabel) + 8,
                Y = 1,
                Width = 35
            };

            var scopeLabel = new Label("Scope:")
            {
                X = Pos.Right(nameField) + 3,
                Y = 1
            };

            scopeRadioGroup = new RadioGroup(new NStack.ustring[] { "_Global", "_Workspace" })
            {
                X = Pos.Right(scopeLabel) + 1,
                Y = 1,
                DisplayMode = DisplayModeLayout.Horizontal,
                SelectedItem = skill.Scope == SkillScope.Workspace ? 1 : 0
            };

            var descriptionLabel = new Label("Description:")
            {
                X = 1,
                Y = 3
            };

            descriptionField = new TextField(skill.Description)
            {
                X = Pos.Right(descriptionLabel) + 1,
                Y = 3,
                Width = Dim.Fill(1)
            };

            var triggersLabel = new Label("Triggers:")
            {
                X = 1,
                Y = 5
            };

            triggersField = new TextField(string.Join(", ", skill.Triggers))
            {
                X = Pos.Right(triggersLabel) + 5,
                Y = 5,
                Width = Dim.Fill(1)
            };

            var triggersHint = new Label("Comma-separated phrases; a matching user message auto-injects the skill. Leave empty for load_skill-only.")
            {
                X = 1,
                Y = 6,
                Width = Dim.Fill(1)
            };

            enabledCheckBox = new CheckBox("Enabled")
            {
                X = 1,
                Y = 8,
                Checked = skill.Enabled
            };

            orchestratorCheckBox = new CheckBox("Apply to Orchestrator")
            {
                X = Pos.Right(enabledCheckBox) + 3,
                Y = 8,
                Checked = skill.ApplyToOrchestrator
            };

            subAgentsCheckBox = new CheckBox("Apply to Sub-Agents")
            {
                X = Pos.Right(orchestratorCheckBox) + 3,
                Y = 8,
                Checked = skill.ApplyToSubAgents
            };

            var typesLabel = new Label("Sub-agent types (none checked = all types):")
            {
                X = 1,
                Y = 10
            };

            agentTypeCheckBoxes = new List<CheckBox>();
            View previous = typesLabel;
            foreach (var typeName in AgentTypeRegistry.Names)
            {
                var isChecked = skill.SubAgentTypes != null &&
                                skill.SubAgentTypes.Contains(typeName, StringComparer.OrdinalIgnoreCase);
                var checkBox = new CheckBox(typeName)
                {
                    X = agentTypeCheckBoxes.Count == 0 ? Pos.Right(typesLabel) + 1 : Pos.Right(previous) + 2,
                    Y = 10,
                    Checked = isChecked
                };
                agentTypeCheckBoxes.Add(checkBox);
                previous = checkBox;
            }

            var contentLabel = new Label("Content (instructions and reference material injected into the conversation):")
            {
                X = 1,
                Y = 12
            };

            contentTextView = new TextView()
            {
                X = 1,
                Y = 13,
                Width = Dim.Fill(1),
                Height = 9,
                Text = skill.Content
            };

            var buttonSeparator = new Label(new string('─', 88))
            {
                X = 0,
                Y = Pos.Bottom(contentTextView) + 1,
                Width = Dim.Fill()
            };

            saveButton = new Button("_Save", true)
            {
                X = Pos.Center() - 10,
                Y = Pos.Bottom(buttonSeparator) + 1
            };
            saveButton.Clicked += () => OnSaveClicked();

            cancelButton = new Button("_Cancel")
            {
                X = Pos.Right(saveButton) + 2,
                Y = Pos.Top(saveButton)
            };
            cancelButton.Clicked += () => Application.RequestStop();

            Add(nameLabel, nameField, scopeLabel, scopeRadioGroup,
                descriptionLabel, descriptionField,
                triggersLabel, triggersField, triggersHint,
                enabledCheckBox, orchestratorCheckBox, subAgentsCheckBox,
                typesLabel);
            foreach (var checkBox in agentTypeCheckBoxes)
            {
                Add(checkBox);
            }
            Add(contentLabel, contentTextView, buttonSeparator, saveButton, cancelButton);

            nameField.SetFocus();
        }

        private async void OnSaveClicked()
        {
            skill.Name = nameField.Text.ToString() ?? "";
            skill.Description = descriptionField.Text.ToString() ?? "";
            skill.Content = contentTextView.Text.ToString() ?? "";
            skill.Enabled = enabledCheckBox.Checked;
            skill.ApplyToOrchestrator = orchestratorCheckBox.Checked;
            skill.ApplyToSubAgents = subAgentsCheckBox.Checked;
            skill.Scope = scopeRadioGroup.SelectedItem == 1 ? SkillScope.Workspace : SkillScope.Global;

            skill.Triggers = (triggersField.Text.ToString() ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

            var checkedTypes = AgentTypeRegistry.Names
                .Where((name, index) => agentTypeCheckBoxes[index].Checked)
                .ToList();
            skill.SubAgentTypes = checkedTypes.Count == 0 ? null : checkedTypes;

            try
            {
                ResultSkill = isEditMode
                    ? await SkillManager.UpdateSkillAsync(skill)
                    : await SkillManager.CreateSkillAsync(skill);

                Application.RequestStop();
            }
            catch (Exception ex)
            {
                MessageBox.ErrorQuery("Error", $"Failed to save skill: {ex.Message}", "OK");
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Terminal.Gui;
using Saturn.Configuration;
using Saturn.Configuration.Objects;
using Saturn.Providers;

namespace Saturn.UI.Dialogs
{
    /// <summary>
    /// Generic provider picker. The provider list comes from the registry and the
    /// settings fields are rendered from each provider's descriptors, so new providers
    /// get their UI for free. Apply validates the connection before swapping; a failed
    /// connect leaves the current provider untouched.
    /// </summary>
    public class ProviderSettingsDialog : Dialog
    {
        private readonly LlmClientManager manager;
        private readonly PersistedAgentConfiguration? persistedConfig;
        private readonly List<ILlmProvider> providers;

        private RadioGroup providerRadio = null!;
        private View settingsArea = null!;
        private Label statusLabel = null!;
        private Button testButton = null!;
        private Button applyButton = null!;
        private Button cancelButton = null!;
        private readonly List<(ProviderSettingDescriptor Descriptor, TextField Field)> settingFields = new();
        private bool isBusy;

        public bool Applied { get; private set; }
        public ProviderSettings? AppliedSettings { get; private set; }

        /// <summary>
        /// While a test/apply is in flight the dialog must not close: dismissing it
        /// mid-swap would leave the manager half-applied and the later RequestStop
        /// would land on the main window instead.
        /// </summary>
        public override bool ProcessKey(KeyEvent keyEvent)
        {
            if (isBusy && keyEvent.Key == Key.Esc)
            {
                return true;
            }
            return base.ProcessKey(keyEvent);
        }

        public ProviderSettingsDialog(LlmClientManager manager, PersistedAgentConfiguration? persistedConfig)
            : base("Provider Settings", 78, 24)
        {
            ColorScheme = Colors.Dialog;
            this.manager = manager;
            this.persistedConfig = persistedConfig;
            providers = ProviderRegistry.All.ToList();

            InitializeComponents();
        }

        private void InitializeComponents()
        {
            var providerLabel = new Label("Provider:")
            {
                X = 1,
                Y = 1
            };

            providerRadio = new RadioGroup(providers.Select(p => (NStack.ustring)p.DisplayName).ToArray())
            {
                X = 1,
                Y = 2
            };

            var activeIndex = providers.FindIndex(p =>
                string.Equals(p.Name, manager.ActiveProviderName, StringComparison.OrdinalIgnoreCase));
            if (activeIndex >= 0)
            {
                providerRadio.SelectedItem = activeIndex;
            }

            providerRadio.SelectedItemChanged += (args) => RebuildSettingFields();

            var separator = new Label(new string('─', 76))
            {
                X = 0,
                Y = 2 + providers.Count + 1,
                Width = Dim.Fill()
            };

            settingsArea = new View()
            {
                X = 1,
                Y = Pos.Bottom(separator) + 1,
                Width = Dim.Fill(1),
                Height = 9
            };

            statusLabel = new Label("")
            {
                X = 1,
                Y = Pos.Bottom(settingsArea) + 1,
                Width = Dim.Fill(1),
                Height = 2
            };

            testButton = new Button("_Test Connection")
            {
                X = Pos.Center() - 22,
                Y = Pos.Bottom(statusLabel) + 1
            };
            testButton.Clicked += () => OnTestClicked();

            applyButton = new Button("_Apply", true)
            {
                X = Pos.Right(testButton) + 2,
                Y = Pos.Top(testButton)
            };
            applyButton.Clicked += () => OnApplyClicked();

            cancelButton = new Button("_Cancel")
            {
                X = Pos.Right(applyButton) + 2,
                Y = Pos.Top(testButton)
            };
            cancelButton.Clicked += () => Application.RequestStop();

            Add(providerLabel, providerRadio, separator, settingsArea, statusLabel,
                testButton, applyButton, cancelButton);

            RebuildSettingFields();
        }

        private ILlmProvider SelectedProvider => providers[Math.Max(0, providerRadio.SelectedItem)];

        private void RebuildSettingFields()
        {
            var oldViews = settingsArea.Subviews.ToList();
            settingsArea.RemoveAll();
            foreach (var view in oldViews)
            {
                view.Dispose();
            }
            settingFields.Clear();

            var provider = SelectedProvider;
            var saved = ConfigurationManager.GetProviderSettings(persistedConfig, provider.Name);

            // Prefer the values the live connection was actually built with.
            if (string.Equals(provider.Name, manager.ActiveProviderName, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var kvp in manager.ActiveSettings.Values)
                {
                    saved.Values[kvp.Key] = kvp.Value;
                }
            }

            int y = 0;
            foreach (var descriptor in provider.SettingDescriptors)
            {
                var label = new Label($"{descriptor.Label}:")
                {
                    X = 0,
                    Y = y
                };

                var field = new TextField(saved.Get(descriptor.Key) ?? "")
                {
                    X = 28,
                    Y = y,
                    Width = Dim.Fill(1),
                    Secret = descriptor.Kind == ProviderSettingKind.Secret
                };

                settingsArea.Add(label, field);
                settingFields.Add((descriptor, field));
                y++;

                var hints = new List<string>();
                if (!string.IsNullOrWhiteSpace(descriptor.EnvironmentVariable))
                {
                    var envSet = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(descriptor.EnvironmentVariable));
                    hints.Add(envSet
                        ? $"blank = env {descriptor.EnvironmentVariable}"
                        : $"env: {descriptor.EnvironmentVariable}");
                }
                if (!string.IsNullOrWhiteSpace(descriptor.DefaultValue))
                {
                    hints.Add($"default: {descriptor.DefaultValue}");
                }

                if (hints.Count > 0)
                {
                    var hintLabel = new Label($"  ({string.Join(" | ", hints)})")
                    {
                        X = 0,
                        Y = y,
                        Width = Dim.Fill(1),
                        ColorScheme = Colors.Menu
                    };
                    settingsArea.Add(hintLabel);
                    y++;
                }

                y++;
            }

            statusLabel.Text = "";
            settingsArea.SetNeedsDisplay();
        }

        private ProviderSettings CollectSettings()
        {
            var settings = new ProviderSettings();
            foreach (var (descriptor, field) in settingFields)
            {
                settings.Set(descriptor.Key, field.Text?.ToString());
            }
            return settings;
        }

        private void SetBusy(bool busy)
        {
            isBusy = busy;
            testButton.Enabled = !busy;
            applyButton.Enabled = !busy;
            cancelButton.Enabled = !busy;
        }

        private void SetStatus(string message)
        {
            Application.MainLoop.Invoke(() =>
            {
                statusLabel.Text = message;
                Application.Refresh();
            });
        }

        private async void OnTestClicked()
        {
            var provider = SelectedProvider;
            var settings = CollectSettings();

            SetBusy(true);
            SetStatus($"Testing connection to {provider.DisplayName}...");

            try
            {
                var client = await provider.CreateClientAsync(settings);
                try
                {
                    var reachable = await client.ValidateConnectionAsync();
                    SetStatus(reachable
                        ? $"Connection to {provider.DisplayName} OK."
                        : $"Could not connect to {provider.DisplayName}. Check the settings and that the service is running.");
                }
                finally
                {
                    client.Dispose();
                }
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message);
            }
            finally
            {
                Application.MainLoop.Invoke(() => SetBusy(false));
            }
        }

        private async void OnApplyClicked()
        {
            var provider = SelectedProvider;
            var settings = CollectSettings();

            SetBusy(true);
            SetStatus($"Connecting to {provider.DisplayName}...");

            try
            {
                var result = await manager.SwapAsync(provider.Name, settings);
                if (result.Success)
                {
                    Applied = true;
                    AppliedSettings = settings;
                    Application.MainLoop.Invoke(() =>
                    {
                        SetBusy(false);
                        // Only stop this dialog; if it somehow already closed, stopping
                        // the current toplevel would take down the main window.
                        if (Application.Current == this)
                        {
                            Application.RequestStop();
                        }
                    });
                }
                else
                {
                    SetStatus(result.Error ?? "Failed to connect.");
                    Application.MainLoop.Invoke(() => SetBusy(false));
                }
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message);
                Application.MainLoop.Invoke(() => SetBusy(false));
            }
        }
    }
}

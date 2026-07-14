using System;
using System.Collections.Generic;
using System.Linq;
using Terminal.Gui;
using Saturn.Configuration;
using Saturn.Configuration.Objects;
using Saturn.Providers;
using Saturn.Tools.Search;

namespace Saturn.UI.Dialogs
{
    public class SearchProviderSettingsDialog : Dialog
    {
        private PersistedAgentConfiguration? persistedConfig;
        private readonly List<ISearchProvider> providers;

        private RadioGroup providerRadio = null!;
        private View settingsArea = null!;
        private Label statusLabel = null!;
        private Button testButton = null!;
        private Button applyButton = null!;
        private Button cancelButton = null!;
        private readonly List<(ProviderSettingDescriptor Descriptor, TextField Field)> settingFields = new();
        private bool isBusy;

        public bool Applied { get; private set; }

        public override bool ProcessKey(KeyEvent keyEvent)
        {
            if (isBusy && keyEvent.Key == Key.Esc)
            {
                return true;
            }
            return base.ProcessKey(keyEvent);
        }

        public SearchProviderSettingsDialog(PersistedAgentConfiguration? persistedConfig)
            : base("Web Search Provider", 78, 24)
        {
            ColorScheme = Colors.Dialog;
            this.persistedConfig = persistedConfig;
            providers = SearchProviderRegistry.All.ToList();

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
                string.Equals(p.Name, persistedConfig?.SearchProvider, StringComparison.OrdinalIgnoreCase));
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

            testButton = new Button("_Test Search")
            {
                X = Pos.Center() - 20,
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

        private ISearchProvider SelectedProvider => providers[Math.Max(0, providerRadio.SelectedItem)];

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
            var saved = ConfigurationManager.GetSearchProviderSettings(persistedConfig, provider.Name);

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
            SetStatus($"Testing search via {provider.DisplayName}...");

            try
            {
                var response = await provider.SearchAsync("saturn agent test", 1, settings, SearchHttpClient);
                SetStatus($"{provider.DisplayName} OK — {response.Results.Count} result(s) returned.");
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
            SetStatus($"Saving {provider.DisplayName}...");

            try
            {
                await ConfigurationManager.SaveSearchProviderSelectionAsync(provider.Name, settings);
                Applied = true;
                persistedConfig = await ConfigurationManager.LoadConfigurationAsync();
                Application.MainLoop.Invoke(() =>
                {
                    SetBusy(false);
                    if (Application.Current == this)
                    {
                        Application.RequestStop();
                    }
                });
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message);
                Application.MainLoop.Invoke(() => SetBusy(false));
            }
        }

        private static readonly System.Net.Http.HttpClient SearchHttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }
}

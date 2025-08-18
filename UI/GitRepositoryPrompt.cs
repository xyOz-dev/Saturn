using Saturn.Core;
using System;
using System.Threading.Tasks;
using Terminal.Gui;

namespace Saturn.UI
{
    public static class GitRepositoryPrompt
    {
        public static Task<bool> ShowPrompt()
        {
            var userChoice = false;
            var initializationComplete = false;

            Application.Init();

            try
            {
                var top = Application.Top;
                top.ColorScheme = Colors.Base;

                var width = Math.Min(80, Application.Top.Frame.Width - 4);
                var height = 14;
                var x = (Application.Top.Frame.Width - width) / 2;
                var y = (Application.Top.Frame.Height - height) / 2;

                var dialog = new Dialog()
                {
                    Title = "Git Repository Required",
                    X = x,
                    Y = y,
                    Width = width,
                    Height = height,
                    ColorScheme = new ColorScheme()
                    {
                        Normal = Application.Driver.MakeAttribute(Color.White, Color.DarkGray),
                        Focus = Application.Driver.MakeAttribute(Color.Black, Color.Gray),
                        HotNormal = Application.Driver.MakeAttribute(Color.BrightYellow, Color.DarkGray),
                        HotFocus = Application.Driver.MakeAttribute(Color.BrightYellow, Color.Gray)
                    }
                };

                var warningIcon = new Label("⚠️")
                {
                    X = 2,
                    Y = 1,
                    ColorScheme = new ColorScheme()
                    {
                        Normal = Application.Driver.MakeAttribute(Color.BrightYellow, Color.DarkGray)
                    }
                };

                var messageLabel = new Label()
                {
                    X = 5,
                    Y = 1,
                    Width = Dim.Fill() - 2,
                    Height = 4,
                    Text = "A Git Repository is REQUIRED to use this application currently\nto protect you from losing your work.\n\nNo .git folder was found in the current directory.",
                    ColorScheme = new ColorScheme()
                    {
                        Normal = Application.Driver.MakeAttribute(Color.White, Color.DarkGray)
                    }
                };

                var pathLabel = new Label($"Directory: {Environment.CurrentDirectory}")
                {
                    X = 2,
                    Y = 6,
                    Width = Dim.Fill() - 2,
                    ColorScheme = new ColorScheme()
                    {
                        Normal = Application.Driver.MakeAttribute(Color.Gray, Color.DarkGray)
                    }
                };

                var progressLabel = new Label("")
                {
                    X = Pos.Center(),
                    Y = 8,
                    Width = Dim.Fill() - 4,
                    TextAlignment = TextAlignment.Centered,
                    Visible = false,
                    ColorScheme = new ColorScheme()
                    {
                        Normal = Application.Driver.MakeAttribute(Color.BrightCyan, Color.DarkGray)
                    }
                };

                var initButton = new Button("Initialize Repository", true)
                {
                    X = Pos.Center() - 20,
                    Y = Pos.Bottom(dialog) - 3,
                    ColorScheme = new ColorScheme()
                    {
                        Normal = Application.Driver.MakeAttribute(Color.Black, Color.BrightGreen),
                        Focus = Application.Driver.MakeAttribute(Color.White, Color.Green),
                        HotNormal = Application.Driver.MakeAttribute(Color.Black, Color.BrightGreen),
                        HotFocus = Application.Driver.MakeAttribute(Color.White, Color.Green)
                    }
                };

                var exitButton = new Button("Exit")
                {
                    X = Pos.Center() + 5,
                    Y = Pos.Bottom(dialog) - 3,
                    ColorScheme = new ColorScheme()
                    {
                        Normal = Application.Driver.MakeAttribute(Color.White, Color.DarkGray),
                        Focus = Application.Driver.MakeAttribute(Color.Black, Color.Gray),
                        HotNormal = Application.Driver.MakeAttribute(Color.White, Color.DarkGray),
                        HotFocus = Application.Driver.MakeAttribute(Color.Black, Color.Gray)
                    }
                };

                initButton.Clicked += async () =>
                {
                    initButton.Enabled = false;
                    exitButton.Enabled = false;
                    progressLabel.Visible = true;

                    if (!GitManager.IsGitInstalled())
                    {
                        progressLabel.Text = "❌ Git is not installed. Please install Git from https://git-scm.com/downloads";
                        progressLabel.ColorScheme = new ColorScheme()
                        {
                            Normal = Application.Driver.MakeAttribute(Color.BrightRed, Color.DarkGray)
                        };
                        await Task.Delay(3000);
                        Application.RequestStop();
                        return;
                    }

                    progressLabel.Text = "🔄 Initializing Git repository...";
                    Application.Refresh();

                    var result = await Task.Run(() => GitManager.InitializeRepository());
                    
                    if (result.success)
                    {
                        progressLabel.Text = "✓ Repository initialized successfully!";
                        progressLabel.ColorScheme = new ColorScheme()
                        {
                            Normal = Application.Driver.MakeAttribute(Color.BrightGreen, Color.DarkGray)
                        };
                        userChoice = true;
                        initializationComplete = true;
                        
                        Application.Refresh();
                        await Task.Delay(1500);
                        Application.RequestStop();
                    }
                    else
                    {
                        progressLabel.Text = $"❌ {result.message}";
                        progressLabel.ColorScheme = new ColorScheme()
                        {
                            Normal = Application.Driver.MakeAttribute(Color.BrightRed, Color.DarkGray)
                        };
                        exitButton.Enabled = true;
                    }
                };

                exitButton.Clicked += () =>
                {
                    userChoice = false;
                    Application.RequestStop();
                };

                dialog.Add(warningIcon, messageLabel, pathLabel, progressLabel);
                dialog.AddButton(initButton);
                dialog.AddButton(exitButton);

                top.Add(dialog);
                
                Application.Run();
            }
            finally
            {
                Application.Shutdown();
            }

            return Task.FromResult(userChoice && initializationComplete);
        }
    }
}
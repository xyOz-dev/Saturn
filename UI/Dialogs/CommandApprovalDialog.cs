using System;
using Terminal.Gui;

namespace Saturn.UI.Dialogs
{
    public class CommandApprovalDialog : Dialog
    {
        private TextView commandTextView = null!;
        private Label workingDirLabel = null!;
        private Label warningLabel = null!;
        private Button approveButton = null!;
        private Button denyButton = null!;
        
        public bool Approved { get; private set; }
        
        public CommandApprovalDialog(string command, string? workingDirectory = null)
            : base("Command Approval Required", 80, 16)
        {
            ColorScheme = Colors.Dialog;
            Approved = false;
            
            InitializeComponents(command, workingDirectory);
        }
        
        private void InitializeComponents(string command, string? workingDirectory)
        {
            var headerLabel = new Label("An agent is requesting to execute the following command:")
            {
                X = 1,
                Y = 1,
                Width = Dim.Fill(1)
            };
            Add(headerLabel);
            
            var commandFrame = new FrameView("Command")
            {
                X = 1,
                Y = 3,
                Width = Dim.Fill(1),
                Height = 5
            };
            
            commandTextView = new TextView()
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill(),
                Text = command,
                ReadOnly = true,
                WordWrap = true,
                ColorScheme = new ColorScheme
                {
                    Normal = Application.Driver.MakeAttribute(Color.White, Color.DarkGray),
                    Focus = Application.Driver.MakeAttribute(Color.White, Color.DarkGray)
                }
            };
            commandFrame.Add(commandTextView);
            Add(commandFrame);
            
            workingDirLabel = new Label($"Working Directory: {workingDirectory ?? "Current Directory"}")
            {
                X = 1,
                Y = Pos.Bottom(commandFrame) + 1,
                Width = Dim.Fill(1)
            };
            Add(workingDirLabel);
            
            warningLabel = new Label("Review this command carefully before approving!")
            {
                X = 1,
                Y = Pos.Bottom(workingDirLabel) + 1,
                Width = Dim.Fill(1),
                ColorScheme = new ColorScheme
                {
                    Normal = Application.Driver.MakeAttribute(Color.BrightYellow, Color.Black)
                }
            };
            Add(warningLabel);
            
            approveButton = new Button("Approve")
            {
                X = Pos.Center() - 8,
                Y = Pos.AnchorEnd(2),
                IsDefault = false
            };
            approveButton.Clicked += OnApproveClicked;
            Add(approveButton);
            
            denyButton = new Button("Deny")
            {
                X = Pos.Center() + 8,
                Y = Pos.AnchorEnd(2),
                IsDefault = true
            };
            denyButton.Clicked += OnDenyClicked;
            Add(denyButton);
            
            denyButton.SetFocus();
        }
        
        private void OnApproveClicked()
        {
            Approved = true;
            Application.RequestStop();
        }
        
        private void OnDenyClicked()
        {
            Approved = false;
            Application.RequestStop();
        }
        
        public override bool ProcessKey(KeyEvent keyEvent)
        {
            if (keyEvent.Key == Key.Esc)
            {
                Approved = false;
                Application.RequestStop();
                return true;
            }
            
            return base.ProcessKey(keyEvent);
        }
    }
}
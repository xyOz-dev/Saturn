using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Saturn.Providers.Models;

namespace Saturn.Providers.ClaudeCLI
{
    public class ClaudeCLIProvider : ILLMProvider
    {
        private ClaudeCLIClient _client;
        
        public string Name => "Claude CLI";
        public bool RequiresAuthentication => false; // CLI handles its own auth
        public bool IsAuthenticated => CheckCLIAvailable();
        
        public async Task<bool> AuthenticateAsync()
        {
            // Check if Claude CLI is available and authenticated
            return await Task.FromResult(CheckCLIAvailable());
        }
        
        public async Task LogoutAsync()
        {
            // Claude CLI manages its own sessions
            await Task.CompletedTask;
        }
        
        public ILLMClient CreateClient()
        {
            if (_client == null)
            {
                _client = new ClaudeCLIClient();
            }
            return _client;
        }
        
        public void Dispose()
        {
            _client?.Dispose();
        }
        
        private bool CheckCLIAvailable()
        {
            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "claude",
                        Arguments = "--version",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };
                
                process.Start();
                process.WaitForExit(5000);
                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
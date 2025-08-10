using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Saturn.Web
{
    public class StaticFileHandler
    {
        private readonly string _webRoot;
        private readonly Dictionary<string, string> _mimeTypes;

        public StaticFileHandler()
        {
            _webRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot");
            
            if (!Directory.Exists(_webRoot))
            {
                var alternativePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
                if (Directory.Exists(alternativePath))
                {
                    _webRoot = alternativePath;
                }
                else
                {
                    var projectPath = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "wwwroot");
                    if (Directory.Exists(projectPath))
                    {
                        _webRoot = Path.GetFullPath(projectPath);
                    }
                }
            }
            
            _mimeTypes = new Dictionary<string, string>
            {
                { ".html", "text/html" },
                { ".css", "text/css" },
                { ".js", "application/javascript" },
                { ".json", "application/json" },
                { ".png", "image/png" },
                { ".jpg", "image/jpeg" },
                { ".jpeg", "image/jpeg" },
                { ".gif", "image/gif" },
                { ".svg", "image/svg+xml" },
                { ".ico", "image/x-icon" },
                { ".txt", "text/plain" }
            };
        }

        public async Task HandleRequest(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;
            var path = request.Url.AbsolutePath;

            if (path == "/")
            {
                path = "/index.html";
            }

            path = path.TrimStart('/');
            var filePath = Path.Combine(_webRoot, path.Replace('/', Path.DirectorySeparatorChar));

            if (!filePath.StartsWith(_webRoot))
            {
                await SendErrorResponse(response, 403, "Forbidden");
                return;
            }

            if (!Directory.Exists(_webRoot))
            {
                System.Console.WriteLine($"Warning: wwwroot directory not found at: {_webRoot}");
                await SendErrorResponse(response, 404, $"Static files directory not found. Expected at: {_webRoot}");
                return;
            }

            if (!File.Exists(filePath))
            {
                System.Console.WriteLine($"File not found: {filePath}");
                await SendErrorResponse(response, 404, $"File not found: {path}");
                return;
            }

            try
            {
                var extension = Path.GetExtension(filePath).ToLowerInvariant();
                response.ContentType = _mimeTypes.ContainsKey(extension) 
                    ? _mimeTypes[extension] 
                    : "application/octet-stream";

                using var fileStream = File.OpenRead(filePath);
                response.ContentLength64 = fileStream.Length;
                response.StatusCode = 200;
                
                await fileStream.CopyToAsync(response.OutputStream);
            }
            catch (Exception ex)
            {
                await SendErrorResponse(response, 500, $"Error reading file: {ex.Message}");
            }
        }

        private async Task SendErrorResponse(HttpListenerResponse response, int statusCode, string message)
        {
            response.StatusCode = statusCode;
            response.ContentType = "text/plain";
            var buffer = Encoding.UTF8.GetBytes(message);
            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        }
    }
}
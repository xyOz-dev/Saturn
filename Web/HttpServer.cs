using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Saturn.Web
{
    public class HttpServer
    {
        private readonly HttpListener _listener;
        private readonly int _port;
        private readonly ApiHandler _apiHandler;
        private readonly StaticFileHandler _staticFileHandler;
        private CancellationTokenSource _cancellationTokenSource;
        private Task _listenerTask;

        public HttpServer(int port = 8080)
        {
            _port = port;
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{_port}/");
            _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
            
            _apiHandler = new ApiHandler();
            _staticFileHandler = new StaticFileHandler();
        }

        public void Start()
        {
            try
            {
                _listener.Start();
                Console.WriteLine($"Web UI: http://localhost:{_port}");
                
                _cancellationTokenSource = new CancellationTokenSource();
                _listenerTask = Task.Run(() => HandleRequests(_cancellationTokenSource.Token));
            }
            catch (HttpListenerException ex)
            {
                Console.WriteLine($"Failed to start HTTP server: {ex.Message}");
                throw;
            }
        }

        public void Stop()
        {
            _cancellationTokenSource?.Cancel();
            _listener?.Stop();
            _listenerTask?.Wait(TimeSpan.FromSeconds(5));
        }

        private async Task HandleRequests(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && _listener.IsListening)
            {
                try
                {
                    var contextAsync = _listener.GetContextAsync();
                    var context = await Task.Run(() => contextAsync, cancellationToken);
                    
                    _ = Task.Run(async () => await ProcessRequest(context), cancellationToken);
                }
                catch (HttpListenerException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error handling request: {ex.Message}");
                }
            }
        }

        private async Task ProcessRequest(HttpListenerContext context)
        {
            try
            {
                var request = context.Request;
                var response = context.Response;
                
                response.Headers.Add("Access-Control-Allow-Origin", "*");
                response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
                response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");
                
                if (request.HttpMethod == "OPTIONS")
                {
                    response.StatusCode = 200;
                    response.Close();
                    return;
                }
                
                if (request.Url.AbsolutePath.StartsWith("/api/"))
                {
                    await _apiHandler.HandleRequest(context);
                }
                else
                {
                    await _staticFileHandler.HandleRequest(context);
                }
            }
            catch (Exception ex)
            {
                await SendErrorResponse(context.Response, 500, $"Internal Server Error: {ex.Message}");
            }
            finally
            {
                context.Response.Close();
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
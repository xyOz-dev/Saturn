using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Saturn.Tests.Mocks
{
    public class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new();
        private readonly List<HttpRequestMessage> _requests = new();
        
        public IReadOnlyList<HttpRequestMessage> Requests => _requests;
        
        public void SetupResponse(HttpStatusCode statusCode, object content)
        {
            var json = JsonSerializer.Serialize(content);
            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            _responses.Enqueue(response);
        }
        
        public void SetupTokenRefreshResponse(object tokenResponse)
        {
            SetupResponse(HttpStatusCode.OK, tokenResponse);
        }
        
        public void SetupChatCompletionResponse(object chatResponse)
        {
            SetupResponse(HttpStatusCode.OK, chatResponse);
        }
        
        public void SetupStringResponse(HttpStatusCode statusCode, string content, string contentType = "text/plain")
        {
            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content, Encoding.UTF8, contentType)
            };
            _responses.Enqueue(response);
        }
        
        public void SetupErrorResponse(HttpStatusCode statusCode, string errorMessage)
        {
            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(errorMessage, Encoding.UTF8, "text/plain")
            };
            _responses.Enqueue(response);
        }
        
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            _requests.Add(request);
            
            if (_responses.Count > 0)
            {
                return Task.FromResult(_responses.Dequeue());
            }
            
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
        
        public HttpClient CreateClient()
        {
            return new HttpClient(this);
        }
        
        public void Reset()
        {
            _responses.Clear();
            _requests.Clear();
        }
        
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                while (_responses.Count > 0)
                {
                    _responses.Dequeue()?.Dispose();
                }
            }
            base.Dispose(disposing);
        }
    }
}
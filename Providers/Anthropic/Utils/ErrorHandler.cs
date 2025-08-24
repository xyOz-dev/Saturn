using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Saturn.Providers.Anthropic.Utils
{
    public static class ErrorHandler
    {
        public static async Task<T> ExecuteWithRetryAsync<T>(
            Func<Task<T>> operation,
            int maxRetries = 3,
            CancellationToken cancellationToken = default)
        {
            var attempt = 0;
            Exception lastException = null;

            while (attempt < maxRetries)
            {
                try
                {
                    return await operation();
                }
                catch (HttpRequestException ex) when (ShouldRetry(ex))
                {
                    lastException = ex;
                    attempt++;
                    
                    if (attempt < maxRetries)
                    {
                        var delay = CalculateDelay(attempt);
                        Console.WriteLine($"Request failed (attempt {attempt}/{maxRetries}), retrying in {delay.TotalSeconds}s: {ex.Message}");
                        await Task.Delay(delay, cancellationToken);
                    }
                }
                catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
                {
                    lastException = ex;
                    attempt++;
                    
                    if (attempt < maxRetries)
                    {
                        var delay = CalculateDelay(attempt);
                        Console.WriteLine($"Request timeout (attempt {attempt}/{maxRetries}), retrying in {delay.TotalSeconds}s");
                        await Task.Delay(delay, cancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    // Don't retry for non-transient errors
                    throw new InvalidOperationException($"Anthropic API error: {ex.Message}", ex);
                }
            }

            throw new InvalidOperationException($"Failed after {maxRetries} attempts. Last error: {lastException?.Message}", lastException);
        }

        private static bool ShouldRetry(HttpRequestException ex)
        {
            // Retry on rate limits, server errors, and network issues
            var message = ex.Message.ToLowerInvariant();
            
            // Check for HTTP status codes that warrant retry
            if (message.Contains("429") || // Rate limited
                message.Contains("500") || // Internal server error
                message.Contains("502") || // Bad gateway
                message.Contains("503") || // Service unavailable
                message.Contains("504"))   // Gateway timeout
            {
                return true;
            }

            // Network-related errors
            if (message.Contains("timeout") ||
                message.Contains("connection") ||
                message.Contains("network"))
            {
                return true;
            }

            return false;
        }

        private static TimeSpan CalculateDelay(int attempt)
        {
            // Exponential backoff with jitter
            var baseDelay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
            var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, 1000));
            return baseDelay + jitter;
        }

        public static string ParseErrorMessage(string errorResponse)
        {
            try
            {
                var errorJson = JsonSerializer.Deserialize<JsonElement>(errorResponse);
                
                if (errorJson.TryGetProperty("error", out var errorElement))
                {
                    if (errorElement.TryGetProperty("message", out var messageElement))
                    {
                        return messageElement.GetString() ?? "Unknown API error";
                    }
                    
                    if (errorElement.TryGetProperty("type", out var typeElement))
                    {
                        return $"API Error: {typeElement.GetString()}";
                    }
                }
            }
            catch
            {
                // If parsing fails, return the raw response
                return errorResponse;
            }

            return "Unknown API error";
        }
    }
}
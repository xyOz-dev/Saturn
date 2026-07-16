using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Text.RegularExpressions;
using Saturn.Tools.Core;
using Saturn.Tools.Objects;
using HtmlAgilityPack;
using ReverseMarkdown;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace Saturn.Tools
{
    public class WebFetchTool : ToolBase
    {
        public override string Name => "web_fetch";
        
        public override string Description => @"Fetches and processes web content from a public URL. Converts HTML to readable text/markdown format.

When to use:
- Fetching documentation or API references
- Researching solutions from blog posts or articles
- Retrieving information from web resources
- Gathering context from external sources
- Analyzing web page content or structure

Limits:
- Only public http/https URLs; localhost and private-network addresses are blocked (use execute_command with curl for local servers)
- 30 second timeout, at most 5 redirects
- Content is truncated to 'maxLength' characters (default 50000)
- Successful responses are cached for 5 minutes";

        private const int MaxRedirects = 5;
        private const int MaxResponseBytes = 10 * 1024 * 1024;

        private static string BuildHeadersFingerprint(Dictionary<string, object>? headers)
        {
            if (headers == null || headers.Count == 0)
            {
                return "";
            }

            var canonical = string.Join("\n", headers
                .OrderBy(h => h.Key, StringComparer.OrdinalIgnoreCase)
                .Select(h => $"{h.Key}:{h.Value}"));

            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(canonical));
            return Convert.ToHexString(hash);
        }

        private static async Task<string> ReadBodyBoundedAsync(HttpResponseMessage response)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var stream = await response.Content.ReadAsStreamAsync();
            using var buffered = new MemoryStream();

            var buffer = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, timeout.Token)) > 0)
            {
                buffered.Write(buffer, 0, read);
                if (buffered.Length > MaxResponseBytes)
                {
                    throw new InvalidDataException($"Response exceeded the {MaxResponseBytes / (1024 * 1024)} MB limit and was aborted");
                }
            }

            var charset = response.Content.Headers.ContentType?.CharSet;
            Encoding encoding;
            try
            {
                encoding = string.IsNullOrWhiteSpace(charset)
                    ? Encoding.UTF8
                    : Encoding.GetEncoding(charset.Trim('"'));
            }
            catch (ArgumentException)
            {
                encoding = Encoding.UTF8;
            }

            return encoding.GetString(buffered.ToArray());
        }

        private static readonly HttpClient _httpClient = CreateHttpClient();

        private static readonly ConcurrentDictionary<string, CachedContent> _cache = new();
        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);
        private static readonly HashSet<string> AllowedSchemes = new() { "http", "https" };
        private static readonly HashSet<string> BlockedHosts = new() { "localhost", "127.0.0.1", "0.0.0.0", "::1" };

        private static HttpClient CreateHttpClient()
        {
            var handler = new SocketsHttpHandler
            {
                // Redirects are followed manually so each hop can be re-validated.
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                // Resolving and filtering addresses at connect time closes the DNS-rebinding
                // window between a pre-check and the actual connection.
                ConnectCallback = async (context, cancellationToken) =>
                {
                    var host = context.DnsEndPoint.Host;
                    var addresses = IPAddress.TryParse(host, out var literal)
                        ? new[] { literal }
                        : await Dns.GetHostAddressesAsync(host, cancellationToken);

                    var allowed = addresses.Where(ip => !IsBlockedAddress(ip)).ToArray();
                    if (allowed.Length == 0)
                    {
                        throw new HttpRequestException($"Access to local/private hosts is not allowed: {host}");
                    }

                    var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                    try
                    {
                        await socket.ConnectAsync(allowed, context.DnsEndPoint.Port, cancellationToken);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch
                    {
                        socket.Dispose();
                        throw;
                    }
                }
            };

            var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            client.DefaultRequestHeaders.Add("User-Agent", "Saturn/1.0 (+https://github.com/xyOz-dev/Saturn)");
            client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
            client.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate");
            client.DefaultRequestHeaders.Add("Cache-Control", "no-cache");
            return client;
        }

        protected override Dictionary<string, object> GetParameterProperties()
        {
            return new Dictionary<string, object>
            {
                { "url", new Dictionary<string, object>
                    {
                        { "type", "string" },
                        { "description", "The URL to fetch content from" }
                    }
                },
                { "extractionMode", new Dictionary<string, object>
                    {
                        { "type", "string" },
                        { "enum", new[] { "full", "article", "text", "markdown" } },
                        { "default", "markdown" },
                        { "description", "Content extraction mode: full (entire page), article (main content), text (plain text), markdown (formatted). Default: markdown" }
                    }
                },
                { "maxLength", new Dictionary<string, object>
                    {
                        { "type", "integer" },
                        { "default", 50000 },
                        { "description", "Maximum content length in characters. Default: 50000" }
                    }
                },
                { "includeMetadata", new Dictionary<string, object>
                    {
                        { "type", "boolean" },
                        { "default", true },
                        { "description", "Include page metadata (title, description, og:tags). Default: true" }
                    }
                },
                { "selector", new Dictionary<string, object>
                    {
                        { "type", "string" },
                        { "description", "CSS selector to extract specific content" }
                    }
                },
                { "headers", new Dictionary<string, object>
                    {
                        { "type", "object" },
                        { "description", "Custom HTTP headers as key-value pairs" }
                    }
                },
                { "useCache", new Dictionary<string, object>
                    {
                        { "type", "boolean" },
                        { "description", "Use cached content if available. Default: true" }
                    }
                }
            };
        }

        protected override string[] GetRequiredParameters()
        {
            return new[] { "url" };
        }

        public override string GetDisplaySummary(Dictionary<string, object> parameters)
        {
            var url = GetParameter<string>(parameters, "url", "");
            var mode = GetParameter<string>(parameters, "extractionMode", "markdown");
            
            if (!string.IsNullOrEmpty(url))
            {
                try
                {
                    var uri = new Uri(url);
                    return $"Fetching {uri.Host} [{mode}]";
                }
                catch
                {
                    return $"Fetching URL [{mode}]";
                }
            }
            return "Fetching web content";
        }

        public override async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters)
        {
            var url = GetParameter<string>(parameters, "url");
            var extractionMode = GetParameter<string>(parameters, "extractionMode", "markdown");
            var maxLength = GetParameter<int>(parameters, "maxLength", 50000);
            var includeMetadata = GetParameter<bool>(parameters, "includeMetadata", true);
            var selector = GetParameter<string?>(parameters, "selector", null);
            var headers = GetParameter<Dictionary<string, object>?>(parameters, "headers", null);
            var useCache = GetParameter<bool>(parameters, "useCache", true);

            if (string.IsNullOrWhiteSpace(url))
            {
                return CreateErrorResult("URL cannot be empty");
            }

            try
            {
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                {
                    return CreateErrorResult($"Invalid URL format: {url}");
                }

                var validationError = ValidateUri(uri);
                if (validationError != null)
                {
                    return CreateErrorResult(validationError);
                }

                // Headers are part of the key: a response fetched with auth headers
                // must never be served to a later caller that did not supply them.
                var cacheKey = $"{url}|{extractionMode}|{selector}|{maxLength}|{includeMetadata}|{BuildHeadersFingerprint(headers)}";
                if (useCache && _cache.TryGetValue(cacheKey, out var cached))
                {
                    if (DateTime.UtcNow - cached.CachedAt < CacheDuration)
                    {
                        return cached.Result;
                    }
                    _cache.TryRemove(cacheKey, out _);
                }

                var currentUri = uri;
                HttpResponseMessage response;
                var redirects = 0;

                while (true)
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, currentUri);

                    // Only forward caller-supplied headers to the original origin (scheme,
                    // host, and port) so a redirect can't siphon them off elsewhere,
                    // including https->http downgrades on the same hostname.
                    if (headers != null &&
                        string.Equals(currentUri.Scheme, uri.Scheme, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(currentUri.IdnHost, uri.IdnHost, StringComparison.OrdinalIgnoreCase) &&
                        currentUri.Port == uri.Port)
                    {
                        foreach (var header in headers)
                        {
                            if (header.Value != null)
                            {
                                request.Headers.TryAddWithoutValidation(header.Key, header.Value.ToString());
                            }
                        }
                    }

                    response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

                    var statusCode = (int)response.StatusCode;
                    if (statusCode >= 300 && statusCode < 400 && response.Headers.Location != null)
                    {
                        var location = response.Headers.Location;
                        var nextUri = location.IsAbsoluteUri ? location : new Uri(currentUri, location);
                        response.Dispose();

                        if (++redirects > MaxRedirects)
                        {
                            return CreateErrorResult($"Too many redirects (more than {MaxRedirects})");
                        }

                        var redirectError = ValidateUri(nextUri);
                        if (redirectError != null)
                        {
                            return CreateErrorResult($"Redirect blocked: {redirectError}");
                        }

                        currentUri = nextUri;
                        continue;
                    }

                    break;
                }

                using var responseToDispose = response;

                if (!response.IsSuccessStatusCode)
                {
                    return CreateErrorResult($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
                }

                var contentLength = response.Content.Headers.ContentLength;
                if (contentLength.HasValue && contentLength.Value > MaxResponseBytes)
                {
                    return CreateErrorResult($"Response too large: {contentLength.Value} bytes (limit is {MaxResponseBytes / (1024 * 1024)} MB)");
                }

                var html = await ReadBodyBoundedAsync(response);

                if (string.IsNullOrWhiteSpace(html))
                {
                    return CreateErrorResult("Empty response from server");
                }

                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                var result = ProcessContent(doc, uri, extractionMode, selector!, maxLength, includeMetadata);

                if (useCache && result.Success)
                {
                    _cache[cacheKey] = new CachedContent
                    {
                        Result = result,
                        CachedAt = DateTime.UtcNow
                    };
                }

                return result;
            }
            catch (InvalidDataException ex)
            {
                return CreateErrorResult(ex.Message);
            }
            catch (TaskCanceledException)
            {
                return CreateErrorResult($"Request timeout after 30 seconds");
            }
            catch (HttpRequestException ex)
            {
                return CreateErrorResult($"Network error: {ex.Message}");
            }
            catch (Exception ex)
            {
                return CreateErrorResult($"Error fetching URL: {ex.Message}");
            }
        }

        private ToolResult ProcessContent(HtmlDocument doc, Uri uri, string mode, string selector, int maxLength, bool includeMetadata)
        {
            var metadata = includeMetadata ? ExtractMetadata(doc) : new Dictionary<string, string>();
            string content;

            if (!string.IsNullOrEmpty(selector))
            {
                var selectedNode = doc.DocumentNode.SelectSingleNode(selector);
                if (selectedNode == null)
                {
                    return CreateErrorResult($"No content found for selector: {selector}");
                }
                content = ConvertToMarkdown(selectedNode);
            }
            else
            {
                content = mode switch
                {
                    "full" => ConvertToMarkdown(doc.DocumentNode),
                    "article" => ExtractArticleContent(doc),
                    "text" => ExtractPlainText(doc),
                    _ => ConvertToMarkdown(doc.DocumentNode.SelectSingleNode("//body") ?? doc.DocumentNode)
                };
            }

            if (content.Length > maxLength)
            {
                content = content.Substring(0, maxLength) + "\n\n[Content truncated due to length limit]";
            }

            var output = new StringBuilder();
            
            if (includeMetadata)
            {
                output.AppendLine($"URL: {uri}");
                if (metadata.TryGetValue("title", out var title))
                    output.AppendLine($"Title: {title}");
                if (metadata.TryGetValue("description", out var desc))
                    output.AppendLine($"Description: {desc}");
                output.AppendLine($"Fetched: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
                output.AppendLine();
            }
            
            output.Append(content);

            var resultData = new
            {
                url = uri.ToString(),
                title = metadata.GetValueOrDefault("title", ""),
                metadata = metadata,
                content = content,
                contentLength = content.Length,
                truncated = content.Contains("[Content truncated")
            };

            return CreateSuccessResult(resultData, output.ToString());
        }

        private string ConvertToMarkdown(HtmlNode node)
        {
            RemoveUnwantedElements(node);
            
            var config = new ReverseMarkdown.Config
            {
                UnknownTags = ReverseMarkdown.Config.UnknownTagsOption.PassThrough,
                GithubFlavored = true,
                RemoveComments = true,
                SmartHrefHandling = true
            };
            
            var converter = new Converter(config);
            var markdown = converter.Convert(node.OuterHtml);
            
            markdown = Regex.Replace(markdown, @"\n{3,}", "\n\n");
            markdown = Regex.Replace(markdown, @"[ \t]+", " ");
            
            return markdown.Trim();
        }

        private string ExtractArticleContent(HtmlDocument doc)
        {
            var articleSelectors = new[]
            {
                "//article",
                "//main",
                "//*[@role='main']",
                "//*[@id='content']",
                "//*[@class='content']",
                "//*[@id='main']",
                "//*[@class='main']"
            };

            foreach (var selector in articleSelectors)
            {
                var node = doc.DocumentNode.SelectSingleNode(selector);
                if (node != null)
                {
                    return ConvertToMarkdown(node);
                }
            }

            var body = doc.DocumentNode.SelectSingleNode("//body");
            if (body != null)
            {
                RemoveNavigationElements(body);
                return ConvertToMarkdown(body);
            }

            return ConvertToMarkdown(doc.DocumentNode);
        }

        private string ExtractPlainText(HtmlDocument doc)
        {
            RemoveUnwantedElements(doc.DocumentNode);
            var text = doc.DocumentNode.InnerText;
            text = WebUtility.HtmlDecode(text);
            text = Regex.Replace(text, @"\s+", " ");
            text = Regex.Replace(text, @"\n{2,}", "\n\n");
            return text.Trim();
        }

        private void RemoveUnwantedElements(HtmlNode node)
        {
            var unwantedTags = new[] { "script", "style", "noscript", "iframe", "svg", "canvas" };
            foreach (var tag in unwantedTags)
            {
                foreach (var element in node.SelectNodes($"//{tag}") ?? Enumerable.Empty<HtmlNode>())
                {
                    element.Remove();
                }
            }
        }

        private void RemoveNavigationElements(HtmlNode node)
        {
            var navSelectors = new[]
            {
                "//nav",
                "//header",
                "//footer",
                "//*[@role='navigation']",
                "//*[@class='nav' or @class='navbar' or @class='menu']",
                "//*[@id='nav' or @id='navbar' or @id='menu']"
            };

            foreach (var selector in navSelectors)
            {
                foreach (var element in node.SelectNodes(selector) ?? Enumerable.Empty<HtmlNode>())
                {
                    element.Remove();
                }
            }
        }

        private Dictionary<string, string> ExtractMetadata(HtmlDocument doc)
        {
            var metadata = new Dictionary<string, string>();

            var titleNode = doc.DocumentNode.SelectSingleNode("//title");
            if (titleNode != null)
            {
                metadata["title"] = WebUtility.HtmlDecode(titleNode.InnerText.Trim());
            }

            var metaTags = doc.DocumentNode.SelectNodes("//meta[@name or @property]") ?? Enumerable.Empty<HtmlNode>();
            foreach (var meta in metaTags)
            {
                var name = meta.GetAttributeValue("name", "");
                if (string.IsNullOrEmpty(name))
                {
                    name = meta.GetAttributeValue("property", "");
                }
                var content = meta.GetAttributeValue("content", "");
                
                if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(content))
                {
                    var key = name.ToLower() switch
                    {
                        "description" => "description",
                        "og:title" => "og:title",
                        "og:description" => "og:description",
                        "og:image" => "og:image",
                        "twitter:title" => "twitter:title",
                        "twitter:description" => "twitter:description",
                        "author" => "author",
                        "publish_date" or "article:published_time" => "publishDate",
                        _ => null
                    };

                    if (key != null && !metadata.ContainsKey(key))
                    {
                        metadata[key] = WebUtility.HtmlDecode(content);
                    }
                }
            }

            return metadata;
        }

        private static string? ValidateUri(Uri uri)
        {
            if (!AllowedSchemes.Contains(uri.Scheme.ToLowerInvariant()))
            {
                return $"Blocked URL scheme: {uri.Scheme}";
            }

            if (BlockedHosts.Contains(uri.Host.ToLowerInvariant()) ||
                (IPAddress.TryParse(uri.Host, out var literal) && IsBlockedAddress(literal)))
            {
                return $"Access to local/private hosts is not allowed: {uri.Host}";
            }

            return null;
        }

        private static bool IsBlockedAddress(IPAddress ip)
        {
            if (ip.IsIPv4MappedToIPv6)
            {
                ip = ip.MapToIPv4();
            }

            if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any))
            {
                return true;
            }

            byte[] bytes = ip.GetAddressBytes();

            if (bytes.Length == 4)
            {
                return (bytes[0] == 10) ||
                       (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                       (bytes[0] == 192 && bytes[1] == 168) ||
                       (bytes[0] == 169 && bytes[1] == 254) ||
                       (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127);
            }

            if (bytes.Length == 16)
            {
                return (bytes[0] == 0xfc || bytes[0] == 0xfd) ||
                       (bytes[0] == 0xfe && (bytes[1] & 0xc0) == 0x80);
            }

            return false;
        }
    }
}
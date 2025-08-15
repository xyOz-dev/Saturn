using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Linq;
using System.Text.RegularExpressions;
using Saturn.Tools.Core;
using HtmlAgilityPack;
using ReverseMarkdown;
using System.Collections.Concurrent;
using System.Net;

namespace Saturn.Tools
{
    public class WebFetchTool : ToolBase
    {
        public override string Name => "web_fetch";
        
        public override string Description => @"Fetches and processes web content from any URL. Converts HTML to readable text/markdown format.

When to use:
- Fetching documentation or API references
- Researching solutions from blog posts or articles
- Retrieving information from web resources
- Gathering context from external sources
- Analyzing web page content or structure";

        private static readonly HttpClient _httpClient = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        })
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        private static readonly ConcurrentDictionary<string, CachedContent> _cache = new();
        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);
        private static readonly HashSet<string> BlockedSchemes = new() { "file", "ftp", "mailto", "javascript", "data" };
        private static readonly HashSet<string> BlockedHosts = new() { "localhost", "127.0.0.1", "0.0.0.0", "::1" };

        static WebFetchTool()
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Saturn/1.0 (+https://github.com/xyOz-dev/Saturn)");
            _httpClient.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            _httpClient.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
            _httpClient.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate");
            _httpClient.DefaultRequestHeaders.Add("Cache-Control", "no-cache");
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
                        { "description", "Content extraction mode: full (entire page), article (main content), text (plain text), markdown (formatted). Default: markdown" }
                    }
                },
                { "maxLength", new Dictionary<string, object>
                    {
                        { "type", "integer" },
                        { "description", "Maximum content length in characters. Default: 50000" }
                    }
                },
                { "includeMetadata", new Dictionary<string, object>
                    {
                        { "type", "boolean" },
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
            var selector = GetParameter<string>(parameters, "selector", null);
            var headers = GetParameter<Dictionary<string, object>>(parameters, "headers", null);
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

                if (BlockedSchemes.Contains(uri.Scheme.ToLower()))
                {
                    return CreateErrorResult($"Blocked URL scheme: {uri.Scheme}");
                }

                if (BlockedHosts.Contains(uri.Host.ToLower()) || IsPrivateIP(uri.Host))
                {
                    return CreateErrorResult($"Access to local/private hosts is not allowed: {uri.Host}");
                }

                var cacheKey = $"{url}|{extractionMode}|{selector}";
                if (useCache && _cache.TryGetValue(cacheKey, out var cached))
                {
                    if (DateTime.UtcNow - cached.CachedAt < CacheDuration)
                    {
                        return cached.Result;
                    }
                    _cache.TryRemove(cacheKey, out _);
                }

                var request = new HttpRequestMessage(HttpMethod.Get, uri);
                
                if (headers != null)
                {
                    foreach (var header in headers)
                    {
                        if (header.Value != null)
                        {
                            request.Headers.TryAddWithoutValidation(header.Key, header.Value.ToString());
                        }
                    }
                }

                var response = await _httpClient.SendAsync(request);
                
                if (!response.IsSuccessStatusCode)
                {
                    return CreateErrorResult($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
                }

                var html = await response.Content.ReadAsStringAsync();
                
                if (string.IsNullOrWhiteSpace(html))
                {
                    return CreateErrorResult("Empty response from server");
                }

                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                var result = ProcessContent(doc, uri, extractionMode, selector, maxLength, includeMetadata);
                
                if (useCache)
                {
                    _cache[cacheKey] = new CachedContent
                    {
                        Result = result,
                        CachedAt = DateTime.UtcNow
                    };
                }

                return result;
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
            
            var config = new Config
            {
                UnknownTags = Config.UnknownTagsOption.PassThrough,
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
                var name = meta.GetAttributeValue("name", "") ?? meta.GetAttributeValue("property", "");
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

        private bool IsPrivateIP(string host)
        {
            if (!IPAddress.TryParse(host, out var ip))
            {
                return false;
            }

            byte[] bytes = ip.GetAddressBytes();
            
            if (bytes.Length == 4)
            {
                return (bytes[0] == 10) ||
                       (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                       (bytes[0] == 192 && bytes[1] == 168) ||
                       (bytes[0] == 169 && bytes[1] == 254);
            }
            
            if (bytes.Length == 16)
            {
                return (bytes[0] == 0xfc || bytes[0] == 0xfd) ||
                       (bytes[0] == 0xfe && (bytes[1] & 0xc0) == 0x80);
            }

            return false;
        }

        private class CachedContent
        {
            public ToolResult Result { get; set; }
            public DateTime CachedAt { get; set; }
        }
    }
}
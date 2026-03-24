using Markdig;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace MyCustomUmbracoProject.Controllers
{
    /// Handles AI chat requests by forwarding prompts to the Mistral API
    /// and returning the response to the content page.
    [Route("umbraco/surface/AiSearch")]
    public class AiSearchSurfaceController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<AiSearchSurfaceController> _logger;
        private readonly IMemoryCache _cache;

        /// Constructor — ASP.NET automatically injects these dependencies.
        public AiSearchSurfaceController(
            IConfiguration configuration,
            ILogger<AiSearchSurfaceController> logger,
            IMemoryCache cache)
        {
            _configuration = configuration;
            _logger = logger;
            _cache = cache;
        }

        /// Receives a prompt from the form, calls the Mistral API,
        /// and redirects back to the page with the response in TempData.
        [HttpPost("Ask")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Ask(string prompt, string model)
        {
            if (string.IsNullOrWhiteSpace(prompt))
            {
                TempData["AiResponse"] = "Please enter a prompt.";
                TempData["UserPrompt"] = prompt;
                return Redirect(Request.Headers["Referer"].ToString());
            }

            // Default to small model if none selected
            var selectedModel = model == "mistral-large-latest"
                ? "mistral-large-latest"
                : "mistral-small-latest";

            // Check cache first
            // If this exact prompt + model was asked before, return cached response
            var cacheKey = $"{selectedModel}:{prompt.Trim().ToLower()}";
            if (_cache.TryGetValue(cacheKey, out string? cachedHtml))
            {
                _logger.LogInformation("Cache hit for prompt: {Prompt}", prompt);
                TempData["AiResponse"] = cachedHtml;
                TempData["UserPrompt"] = prompt;
                TempData["SelectedModel"] = selectedModel;
                return Redirect(Request.Headers["Referer"].ToString());
            }

            var apiKey = _configuration["MistralApiKey"]
                ?? Environment.GetEnvironmentVariable("MISTRAL_API_KEY")
                ?? throw new InvalidOperationException("No Mistral API key configured.");

            var requestBody = JsonSerializer.Serialize(new
            {
                model = selectedModel,
                messages = new[] { new { role = "user", content = prompt } }
            });

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

            // Retry logic for rate limits
            // If the API returns 429 (Too Many Requests), wait 2 seconds and try once more
            HttpResponseMessage response;
            response = await client.PostAsync(
                "https://api.mistral.ai/v1/chat/completions",
                new StringContent(requestBody, Encoding.UTF8, "application/json")
            );

            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                _logger.LogWarning("Rate limit hit, retrying after 2 seconds...");
                await Task.Delay(2000);
                response = await client.PostAsync(
                    "https://api.mistral.ai/v1/chat/completions",
                    new StringContent(requestBody, Encoding.UTF8, "application/json")
                );
            }

            var json = await response.Content.ReadAsStringAsync();

            // Log errors properly
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Mistral API error: {StatusCode} - {Body}",
                    response.StatusCode, json);
                TempData["AiResponse"] = $"API error: {response.StatusCode}";
                TempData["UserPrompt"] = prompt;
                return Redirect(Request.Headers["Referer"].ToString());
            }

            var doc = JsonDocument.Parse(json);
            var answer = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            var html = Markdown.ToHtml(answer ?? "");

            // Store in cache for 1 hour
            // Same prompt won't hit the API again for 60 minutes
            _cache.Set(cacheKey, html, TimeSpan.FromHours(1));
            _logger.LogInformation("Response cached for prompt: {Prompt}", prompt);
            

            // Log the AI response for debugging 
            /*
             // _logger.LogInformation("AI response: {Response}", answer);
            */

            TempData["AiResponse"] = html;
            TempData["UserPrompt"] = prompt;
            TempData["SelectedModel"] = selectedModel;
            return Redirect(Request.Headers["Referer"].ToString());
        }
    }
}
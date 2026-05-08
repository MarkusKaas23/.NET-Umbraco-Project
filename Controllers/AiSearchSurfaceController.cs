using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using MyCustomUmbracoProject.Models;
using MyCustomUmbracoProject.Services;
using System.Net;
using System.Text;
using System.Text.Json;

namespace MyCustomUmbracoProject.Controllers
{
    [Route("umbraco/surface/AiSearch")]
    public class AiSearchSurfaceController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<AiSearchSurfaceController> _logger;
        private readonly IMemoryCache _cache;
        private readonly ChatHistoryService _chatHistory;
        private readonly IHttpClientFactory _httpClientFactory;

        private const string SessionCookie = "chatSessionId";
        private const string UserCookie    = "chatUserId";
        private const string MistralEndpoint = "https://api.mistral.ai/v1/chat/completions";
        private const string DefaultModel = "mistral-small-latest";
        private const string LargeModel   = "mistral-large-latest";

        public AiSearchSurfaceController(
            IConfiguration configuration,
            ILogger<AiSearchSurfaceController> logger,
            IMemoryCache cache,
            ChatHistoryService chatHistory,
            IHttpClientFactory httpClientFactory)
        {
            _configuration = configuration;
            _logger = logger;
            _cache = cache;
            _chatHistory = chatHistory;
            _httpClientFactory = httpClientFactory;
        }

        private string GetOrCreateUserId() => GetOrCreateCookie(UserCookie, DateTimeOffset.UtcNow.AddYears(2));
        private string GetOrCreateSessionId() => GetOrCreateCookie(SessionCookie, DateTimeOffset.UtcNow.AddDays(30));

        private string GetOrCreateCookie(string name, DateTimeOffset expires)
        {
            if (Request.Cookies.TryGetValue(name, out var existing) && !string.IsNullOrEmpty(existing))
                return existing;

            var newId = Guid.NewGuid().ToString("N");
            SetCookie(name, newId, expires);
            return newId;
        }

        private void SetCookie(string name, string value, DateTimeOffset expires)
        {
            Response.Cookies.Append(name, value, new CookieOptions
            {
                Expires = expires,
                HttpOnly = true,
                SameSite = SameSiteMode.Lax
            });
        }

        private IActionResult RedirectToReferer() => Redirect(Request.Headers.Referer.ToString());

        [HttpPost("Ask")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Ask(string prompt, string model)
        {
            var userId    = GetOrCreateUserId();
            var sessionId = GetOrCreateSessionId();

            if (string.IsNullOrWhiteSpace(prompt))
            {
                TempData["SelectedModel"] = model;
                return RedirectToReferer();
            }

            var selectedModel = model == LargeModel ? LargeModel : DefaultModel;

            var cacheKey = $"{selectedModel}:{prompt.Trim().ToLowerInvariant()}";
            if (_cache.TryGetValue(cacheKey, out string? cachedMarkdown))
            {
                _logger.LogInformation("Cache hit for prompt: {Prompt}", prompt);
                _chatHistory.Save(BuildMessage(sessionId, userId, prompt, cachedMarkdown!, selectedModel));
                TempData["SelectedModel"] = selectedModel;
                return RedirectToReferer();
            }

            var apiKey = _configuration["MistralApiKey"]
                ?? Environment.GetEnvironmentVariable("MISTRAL_API_KEY")
                ?? throw new InvalidOperationException("No Mistral API key configured.");

            var history = _chatHistory.GetBySession(sessionId, limit: 10);
            var messages = new List<object>(history.Count * 2 + 1);
            foreach (var msg in history)
            {
                messages.Add(new { role = "user",      content = msg.UserPrompt });
                messages.Add(new { role = "assistant", content = msg.ResponseMarkdown });
            }
            messages.Add(new { role = "user", content = prompt });

            var requestBody = JsonSerializer.Serialize(new { model = selectedModel, messages });

            var response = await SendMistralRequest(apiKey, requestBody);
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Mistral API error: {StatusCode} - {Body}", response.StatusCode, json);
                TempData["AiError"] = $"API error: {response.StatusCode}";
                TempData["SelectedModel"] = selectedModel;
                return RedirectToReferer();
            }

            using var doc = JsonDocument.Parse(json);
            var answerMarkdown = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? "";

            _cache.Set(cacheKey, answerMarkdown, TimeSpan.FromHours(1));
            _chatHistory.Save(BuildMessage(sessionId, userId, prompt, answerMarkdown, selectedModel));

            _logger.LogInformation("Response saved for prompt: {Prompt}", prompt);

            TempData["SelectedModel"] = selectedModel;
            return RedirectToReferer();
        }

        private async Task<HttpResponseMessage> SendMistralRequest(string apiKey, string requestBody)
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

            var response = await PostJson(client, requestBody);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                _logger.LogWarning("Rate limit hit, retrying after 2 seconds...");
                response.Dispose();
                await Task.Delay(2000);
                response = await PostJson(client, requestBody);
            }
            return response;
        }

        private static Task<HttpResponseMessage> PostJson(HttpClient client, string body) =>
            client.PostAsync(MistralEndpoint, new StringContent(body, Encoding.UTF8, "application/json"));

        private static ChatMessage BuildMessage(string sessionId, string userId, string prompt, string markdown, string model) => new()
        {
            SessionId = sessionId,
            UserId = userId,
            UserPrompt = prompt,
            ResponseMarkdown = markdown,
            AiModel = model
        };

        [HttpPost("NewChat")]
        [ValidateAntiForgeryToken]
        public IActionResult NewChat()
        {
            SetCookie(SessionCookie, Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow.AddDays(30));
            return RedirectToReferer();
        }

        [HttpPost("LoadSession")]
        [ValidateAntiForgeryToken]
        public IActionResult LoadSession(string sessionId)
        {
            if (!UserOwnsSession(sessionId))
                return RedirectToReferer();

            SetCookie(SessionCookie, sessionId, DateTimeOffset.UtcNow.AddDays(30));
            return RedirectToReferer();
        }

        [HttpPost("DeleteSession")]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteSession(string sessionId)
        {
            if (!UserOwnsSession(sessionId))
                return RedirectToReferer();

            _chatHistory.ClearSession(sessionId);

            if (Request.Cookies[SessionCookie] == sessionId)
                SetCookie(SessionCookie, Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow.AddDays(30));

            return RedirectToReferer();
        }

        private bool UserOwnsSession(string sessionId) =>
            _chatHistory.GetAllSessions(GetOrCreateUserId()).Any(s => s.SessionId == sessionId);
    }
}

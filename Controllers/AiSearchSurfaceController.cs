using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using MyCustomUmbracoProject.Models;
using MyCustomUmbracoProject.Services;
using System;
using System.Collections.Generic;
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
        private readonly ChatHistoryService _chatHistory;

        private const string SessionCookie = "chatSessionId";
        private const string UserCookie    = "chatUserId";

        public AiSearchSurfaceController(
            IConfiguration configuration,
            ILogger<AiSearchSurfaceController> logger,
            IMemoryCache cache,
            ChatHistoryService chatHistory)
        {
            _configuration = configuration;
            _logger = logger;
            _cache = cache;
            _chatHistory = chatHistory;
        }

        /// Returns the persistent user ID cookie, creating one if needed.
        private string GetOrCreateUserId()
        {
            if (Request.Cookies.TryGetValue(UserCookie, out var existing) && !string.IsNullOrEmpty(existing))
                return existing;

            var newId = Guid.NewGuid().ToString("N");
            Response.Cookies.Append(UserCookie, newId, new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddYears(2),
                HttpOnly = true,
                SameSite = SameSiteMode.Lax
            });
            return newId;
        }

        /// Returns the session ID from the cookie, creating a new one if needed.
        private string GetOrCreateSessionId()
        {
            if (Request.Cookies.TryGetValue(SessionCookie, out var existing) && !string.IsNullOrEmpty(existing))
                return existing;

            var newId = Guid.NewGuid().ToString("N");
            Response.Cookies.Append(SessionCookie, newId, new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddDays(30),
                HttpOnly = true,
                SameSite = SameSiteMode.Lax
            });
            return newId;
        }

        /// Receives a prompt, calls Mistral with full conversation context,
        /// saves the exchange, and redirects back.
        [HttpPost("Ask")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Ask(string prompt, string model)
        {
            var userId    = GetOrCreateUserId();
            var sessionId = GetOrCreateSessionId();

            if (string.IsNullOrWhiteSpace(prompt))
            {
                TempData["SelectedModel"] = model;
                return Redirect(Request.Headers["Referer"].ToString());
            }

            var selectedModel = model == "mistral-large-latest"
                ? "mistral-large-latest"
                : "mistral-small-latest";

            // Check cache — if this exact prompt+model was answered recently, reuse it
            var cacheKey = $"{selectedModel}:{prompt.Trim().ToLower()}";
            if (_cache.TryGetValue(cacheKey, out string? cachedMarkdown))
            {
                _logger.LogInformation("Cache hit for prompt: {Prompt}", prompt);
                _chatHistory.Save(new ChatMessage
                {
                    SessionId = sessionId,
                    UserId    = userId,
                    UserPrompt = prompt,
                    ResponseMarkdown = cachedMarkdown!,
                    AiModel = selectedModel
                });
                TempData["SelectedModel"] = selectedModel;
                return Redirect(Request.Headers["Referer"].ToString());
            }

            var apiKey = _configuration["MistralApiKey"]
                ?? Environment.GetEnvironmentVariable("MISTRAL_API_KEY")
                ?? throw new InvalidOperationException("No Mistral API key configured.");

            // Build message array from history so the AI remembers the conversation
            var history = _chatHistory.GetBySession(sessionId, limit: 10);
            var messages = new List<object>();
            foreach (var msg in history)
            {
                messages.Add(new { role = "user",      content = msg.UserPrompt });
                messages.Add(new { role = "assistant", content = msg.ResponseMarkdown });
            }
            messages.Add(new { role = "user", content = prompt });

            var requestBody = JsonSerializer.Serialize(new
            {
                model = selectedModel,
                messages
            });

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

            // Retry logic for rate limits
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

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Mistral API error: {StatusCode} - {Body}", response.StatusCode, json);
                TempData["AiError"] = $"API error: {response.StatusCode}";
                TempData["SelectedModel"] = selectedModel;
                return Redirect(Request.Headers["Referer"].ToString());
            }

            var doc = JsonDocument.Parse(json);
            var answerMarkdown = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? "";

            // Cache the raw markdown for 1 hour
            _cache.Set(cacheKey, answerMarkdown, TimeSpan.FromHours(1));

            _chatHistory.Save(new ChatMessage
            {
                SessionId = sessionId,
                UserId    = userId,
                UserPrompt = prompt,
                ResponseMarkdown = answerMarkdown,
                AiModel = selectedModel
            });

            _logger.LogInformation("Response saved for prompt: {Prompt}", prompt);

            TempData["SelectedModel"] = selectedModel;
            return Redirect(Request.Headers["Referer"].ToString());
        }

        /// Starts a new chat session without deleting the current one.
        [HttpPost("NewChat")]
        [ValidateAntiForgeryToken]
        public IActionResult NewChat()
        {
            var newId = Guid.NewGuid().ToString("N");
            Response.Cookies.Append(SessionCookie, newId, new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddDays(30),
                HttpOnly = true,
                SameSite = SameSiteMode.Lax
            });
            return Redirect(Request.Headers["Referer"].ToString());
        }

        /// Loads a past session by setting the session cookie to the requested ID.
        [HttpPost("LoadSession")]
        [ValidateAntiForgeryToken]
        public IActionResult LoadSession(string sessionId)
        {
            var userId = GetOrCreateUserId();

            // Verify the session belongs to this user
            var sessions = _chatHistory.GetAllSessions(userId);
            if (!sessions.Any(s => s.SessionId == sessionId))
                return Redirect(Request.Headers["Referer"].ToString());

            Response.Cookies.Append(SessionCookie, sessionId, new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddDays(30),
                HttpOnly = true,
                SameSite = SameSiteMode.Lax
            });
            return Redirect(Request.Headers["Referer"].ToString());
        }

        /// Deletes all messages in a session. If it was the active session, starts a new one.
        [HttpPost("DeleteSession")]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteSession(string sessionId)
        {
            var userId = GetOrCreateUserId();

            // Verify ownership before deleting
            var sessions = _chatHistory.GetAllSessions(userId);
            if (!sessions.Any(s => s.SessionId == sessionId))
                return Redirect(Request.Headers["Referer"].ToString());

            _chatHistory.ClearSession(sessionId);

            // If the deleted session was the active one, start fresh
            if (Request.Cookies[SessionCookie] == sessionId)
            {
                var newId = Guid.NewGuid().ToString("N");
                Response.Cookies.Append(SessionCookie, newId, new CookieOptions
                {
                    Expires = DateTimeOffset.UtcNow.AddDays(30),
                    HttpOnly = true,
                    SameSite = SameSiteMode.Lax
                });
            }

            return Redirect(Request.Headers["Referer"].ToString());
        }
    }
}

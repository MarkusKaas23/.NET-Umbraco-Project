using Markdig;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace MyCustomUmbracoProject.Controllers
{
    [Route("umbraco/surface/AiSearch")]
    public class AiSearchSurfaceController : Controller
    {
        private readonly IConfiguration _configuration;

        public AiSearchSurfaceController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        [HttpPost("Ask")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Ask(string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt))
            {
                TempData["AiResponse"] = "Please enter a prompt.";
                TempData["UserPrompt"] = prompt;
                return Redirect(Request.Headers["Referer"].ToString());
            }

            var apiKey = _configuration["MistralApiKey"]
                ?? Environment.GetEnvironmentVariable("MISTRAL_API_KEY")
                ?? throw new InvalidOperationException("No Mistral API key configured.");

            var requestBody = JsonSerializer.Serialize(new
            {
                model = "mistral-small-latest",
                messages = new[] { new { role = "user", content = prompt } }
            });

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

            var response = await client.PostAsync(
                "https://api.mistral.ai/v1/chat/completions",
                new StringContent(requestBody, Encoding.UTF8, "application/json")
            );

            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
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
            TempData["AiResponse"] = html;
            TempData["UserPrompt"] = prompt;
            return Redirect(Request.Headers["Referer"].ToString());
        }
    }
}
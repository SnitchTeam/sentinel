using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Oxide.Plugins
{
    public class ByokValidator
    {
        private readonly IHttpRequester _requester;
        private readonly IRuntimeBridge? _logger;

        public ByokValidator(IHttpRequester requester, IRuntimeBridge? logger = null)
        {
            _requester = requester ?? throw new ArgumentNullException(nameof(requester));
            _logger = logger;
        }

        public virtual async Task<bool> ValidateAsync(string provider, string endpoint, string apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                _logger?.LogWarning("[Sentinel] No BYOK API key configured. AI features disabled.");
                return false;
            }

            var baseUrl = endpoint.TrimEnd('/');
            var isAnthropic = provider.Equals("anthropic", StringComparison.OrdinalIgnoreCase);
            var url = $"{baseUrl}/models";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Authorization", $"Bearer {apiKey}");

            if (isAnthropic)
            {
                request.Headers.Add("anthropic-version", "2023-06-01");
            }

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var response = await _requester.SendAsync(request, cts.Token).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    _logger?.LogInfo($"[Sentinel] BYOK key accepted for provider {provider}");
                    return true;
                }

                var statusCode = (int)response.StatusCode;
                if (statusCode == 401 || statusCode == 403)
                {
                    _logger?.LogError($"[Sentinel] BYOK key rejected for provider {provider}: HTTP {statusCode}");
                    return false;
                }

                if (isAnthropic && statusCode == 404)
                {
                    return await ValidateAnthropicViaMessagesAsync(baseUrl, apiKey).ConfigureAwait(false);
                }

                _logger?.LogWarning($"[Sentinel] BYOK key validation returned HTTP {statusCode} for provider {provider}");
                return false;
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[Sentinel] BYOK key validation failed for provider {provider}: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> ValidateAnthropicViaMessagesAsync(string baseUrl, string apiKey)
        {
            var url = $"{baseUrl}/messages";
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("Authorization", $"Bearer {apiKey}");
            request.Headers.Add("anthropic-version", "2023-06-01");

            var jsonBody = JsonSerializer.Serialize(new
            {
                model = "claude-3-haiku-20240307",
                messages = new[] { new { role = "user", content = "Hi" } },
                max_tokens = 1
            });

            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var response = await _requester.SendAsync(request, cts.Token).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    _logger?.LogInfo("[Sentinel] BYOK key accepted for provider anthropic (via POST /v1/messages)");
                    return true;
                }

                var statusCode = (int)response.StatusCode;
                if (statusCode == 401 || statusCode == 403)
                {
                    _logger?.LogError($"[Sentinel] BYOK key rejected for provider anthropic: HTTP {statusCode}");
                    return false;
                }

                _logger?.LogWarning($"[Sentinel] BYOK key validation returned HTTP {statusCode} for provider anthropic");
                return false;
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[Sentinel] BYOK key validation failed for provider anthropic: {ex.Message}");
                return false;
            }
        }
    }
}

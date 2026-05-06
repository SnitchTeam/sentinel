using System;

namespace Oxide.Plugins
{
    public partial class Sentinel
    {
        private AiCostTracker? _aiCostTracker;

        public virtual void InitializeAiCostTracker()
        {
            _aiCostTracker = new AiCostTracker(_dbConnection, _runtimeBridge);
        }

        public virtual LlmResponse SendAiRequest(string prompt)
        {
            var config = PluginConfig?.AI ?? new AIConfig();

            // Check cost cap
            if (_aiCostTracker != null)
            {
                var currentSpend = _aiCostTracker.GetDailySpend(DateTime.UtcNow);
                _aiCostTracker.AlertIfNeeded(currentSpend, config.DailyUsdCap);
                if (currentSpend >= config.DailyUsdCap)
                {
                    _runtimeBridge?.LogWarning($"[Sentinel] AI daily cost cap reached (${currentSpend:F2}/{config.DailyUsdCap:F2}). Returning heuristic stub.");
                    return LlmClient.FallbackResponse(prompt);
                }
            }

            if (_llmClient == null || string.IsNullOrWhiteSpace(config.ApiKey))
            {
                return LlmClient.FallbackResponse(prompt);
            }

            // Validate prompt (prompt injection defense)
            try
            {
                PiiRedactor.ValidatePrompt(prompt);
            }
            catch (PromptInjectionException ex)
            {
                _runtimeBridge?.LogWarning($"[Sentinel] Prompt injection blocked: {ex.Message}");
                return new LlmResponse { Success = false, Error = ex.Message, IsFallback = false };
            }

            // Try primary provider
            var primaryRequest = new LlmRequest
            {
                Provider = config.Provider,
                Endpoint = config.Endpoint,
                ApiKey = config.ApiKey,
                Model = config.Model,
                Prompt = prompt
            };

            var primaryResponse = _llmClient.SendAsync(primaryRequest).ConfigureAwait(false).GetAwaiter().GetResult();

            if (primaryResponse.Success && !primaryResponse.IsFallback)
            {
                RecordAiCost(config.Provider, config.Model, prompt, primaryResponse.Content);
                return primaryResponse;
            }

            // Determine if failover is warranted
            bool shouldFailover = primaryResponse.WasTimeout
                || primaryResponse.LastHttpStatusCode == 429
                || (primaryResponse.LastHttpStatusCode >= 500)
                || (primaryResponse.LastHttpStatusCode == null && primaryResponse.IsFallback);

            if (!shouldFailover || string.IsNullOrWhiteSpace(config.FallbackApiKey))
            {
                return primaryResponse;
            }

            // Forward identical redacted payload to Anthropic
            var fallbackRequest = new LlmRequest
            {
                Provider = config.FallbackProvider,
                Endpoint = config.FallbackEndpoint,
                ApiKey = config.FallbackApiKey,
                Model = config.FallbackModel,
                Prompt = prompt
            };

            _runtimeBridge?.LogWarning($"[Sentinel] Primary provider {config.Provider} failed (HTTP {primaryResponse.LastHttpStatusCode}, timeout={primaryResponse.WasTimeout}). Failing over to {config.FallbackProvider}.");

            var fallbackResponse = _llmClient.SendAsync(fallbackRequest).ConfigureAwait(false).GetAwaiter().GetResult();

            if (fallbackResponse.Success && !fallbackResponse.IsFallback)
            {
                RecordAiCost(config.FallbackProvider, config.FallbackModel, prompt, fallbackResponse.Content);
                return fallbackResponse;
            }

            // Both providers failed — log PROVIDER_FAILOVER event with no keys or PII
            var safeError = string.IsNullOrEmpty(fallbackResponse.Error) ? "unknown" : fallbackResponse.Error;
            if (!string.IsNullOrEmpty(config.ApiKey))
                safeError = safeError.Replace(config.ApiKey, "[REDACTED_KEY]");
            if (!string.IsNullOrEmpty(config.FallbackApiKey))
                safeError = safeError.Replace(config.FallbackApiKey, "[REDACTED_KEY]");

            _runtimeBridge?.LogWarning($"[Sentinel] PROVIDER_FAILOVER: Primary provider {config.Provider} failed. Fallback provider {config.FallbackProvider} also failed. Error: {safeError}");

            return LlmClient.FallbackResponse(prompt);
        }

        public virtual void RecordAiCost(string provider, string model, string prompt, string responseContent)
        {
            if (_aiCostTracker == null) return;
            var (inputTokens, outputTokens, costUsd) = CostEstimator.Estimate(provider, model, prompt, responseContent);
            _aiCostTracker.RecordCost(provider, model, inputTokens, outputTokens, costUsd);
        }
    }
}

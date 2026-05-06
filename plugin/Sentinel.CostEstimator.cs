using System;

namespace Oxide.Plugins
{
    public static class CostEstimator
    {
        public static (int inputTokens, int outputTokens, double costUsd) Estimate(string provider, string model, string prompt, string responseContent)
        {
            var inputTokens = Math.Max(1, prompt.Length / 4);
            var outputTokens = Math.Max(1, responseContent.Length / 4);

            double costPer1MInput = 0.15; // default gpt-4o-mini
            double costPer1MOutput = 0.60;

            var modelLower = model.ToLowerInvariant();
            var providerLower = provider.ToLowerInvariant();

            if (modelLower.Contains("gpt-4o") && !modelLower.Contains("mini"))
            {
                costPer1MInput = 5.0;
                costPer1MOutput = 15.0;
            }
            else if (providerLower.Contains("anthropic") || modelLower.Contains("claude"))
            {
                costPer1MInput = 0.25;
                costPer1MOutput = 1.25;
            }
            else if (modelLower.Contains("gpt-3.5") || modelLower.Contains("gpt-35"))
            {
                costPer1MInput = 0.50;
                costPer1MOutput = 1.50;
            }

            var costUsd = (inputTokens * costPer1MInput + outputTokens * costPer1MOutput) / 1_000_000.0;
            return (inputTokens, outputTokens, Math.Round(costUsd, 6));
        }
    }
}

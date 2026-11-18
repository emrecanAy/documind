using DocuMind.Application.Common;
using Microsoft.Extensions.Options;

namespace DocuMind.Application.Features.Chat;

public sealed class CostCalculator(IOptions<AiOptions> options)
{
    public decimal Estimate(string model, int inputTokens, int outputTokens)
    {
        if (!options.Value.Pricing.TryGetValue(model, out var price))
            return 0m;
        return Math.Round(((inputTokens * price.InputPerMillion) + (outputTokens * price.OutputPerMillion)) / 1_000_000m, 6);
    }
}

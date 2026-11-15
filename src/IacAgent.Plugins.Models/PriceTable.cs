using System.Globalization;

using Microsoft.Extensions.Configuration;

namespace IacAgent.Plugins.Models;

/// <summary>USD prices per one million tokens, keyed by model id.</summary>
internal sealed class PriceTable
{
    private readonly Dictionary<string, ModelPrice> _prices;

    private PriceTable(Dictionary<string, ModelPrice> prices)
    {
        _prices = prices;
    }

    /// <summary>Reads the <c>pricing</c> section. An empty section means no prices.</summary>
    public static PriceTable Bind(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var prices = new Dictionary<string, ModelPrice>(StringComparer.Ordinal);
        foreach (var child in configuration.GetSection("Pricing").GetChildren())
        {
            if (string.IsNullOrWhiteSpace(child.Key))
            {
                throw new InvalidOperationException("A pricing entry needs a model id.");
            }

            prices[child.Key] = new ModelPrice(
                ReadRate(child, "inputPerMillion"),
                ReadRate(child, "outputPerMillion"));
        }

        return new PriceTable(prices);
    }

    /// <summary>Returns the USD cost, or null when this model id has no price.</summary>
    public decimal? CostUsd(string modelId, int inputTokens, int outputTokens)
    {
        if (string.IsNullOrWhiteSpace(modelId) || !_prices.TryGetValue(modelId, out var price))
        {
            return null;
        }

        return ((inputTokens * price.InputPerMillion) + (outputTokens * price.OutputPerMillion)) / 1_000_000m;
    }

    private static decimal ReadRate(IConfigurationSection section, string name)
    {
        var text = section[name];
        if (!string.IsNullOrWhiteSpace(text)
            && decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            && value >= 0m)
        {
            return value;
        }

        throw new InvalidOperationException(
            $"Pricing for '{section.Key}' needs {name} as a number that is zero or greater.");
    }

    private readonly record struct ModelPrice(decimal InputPerMillion, decimal OutputPerMillion);
}

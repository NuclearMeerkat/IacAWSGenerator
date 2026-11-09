using System.Globalization;
using System.Text.Json;

namespace IacAgent.Plugins.Gates.Infracost;

/// <summary>Reads the monthly total from <c>infracost scan --json</c>.</summary>
internal static class InfracostOutputParser
{
    public static InfracostEstimate Parse(string output)
    {
        using var document = JsonDocument.Parse(ExtractJson(output));
        var root = document.RootElement;
        var currency = ReadString(root, "currency") ?? "USD";
        var total = ReadCost(root, "totalMonthlyCost");
        if (root.TryGetProperty("summary", out var summary))
        {
            total ??= ReadCost(summary, "total_monthly_cost");
        }

        var resources = new List<InfracostResourceCost>();
        if (root.TryGetProperty("projects", out var projects) && projects.ValueKind == JsonValueKind.Array)
        {
            foreach (var project in projects.EnumerateArray())
            {
                if (project.TryGetProperty("breakdown", out var breakdown))
                {
                    total ??= ReadCost(breakdown, "totalMonthlyCost");
                    ReadLegacyResources(breakdown, resources);
                }

                ReadScanResources(project, resources);
            }
        }

        var largest = resources
            .OrderByDescending(resource => resource.MonthlyCost)
            .Take(5)
            .ToList();
        return new InfracostEstimate(total, currency, largest);
    }

    private static void ReadScanResources(JsonElement project, List<InfracostResourceCost> resources)
    {
        if (!project.TryGetProperty("resources", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var item in items.EnumerateArray())
        {
            var name = ReadString(item, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            decimal cost = 0;
            var found = false;
            if (item.TryGetProperty("cost_components", out var components) && components.ValueKind == JsonValueKind.Array)
            {
                foreach (var component in components.EnumerateArray())
                {
                    var part = ReadCost(component, "total_monthly_cost");
                    if (part is not null)
                    {
                        cost += part.Value;
                        found = true;
                    }
                }
            }

            if (!found)
            {
                var monthly = ReadCost(item, "monthlyCost");
                if (monthly is null)
                {
                    continue;
                }

                cost = monthly.Value;
            }

            if (cost > 0)
            {
                resources.Add(new InfracostResourceCost(name, cost));
            }
        }
    }

    private static void ReadLegacyResources(JsonElement breakdown, List<InfracostResourceCost> resources)
    {
        if (!breakdown.TryGetProperty("resources", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var item in items.EnumerateArray())
        {
            var name = ReadString(item, "name");
            var cost = ReadCost(item, "monthlyCost");
            if (!string.IsNullOrWhiteSpace(name) && cost is not null)
            {
                resources.Add(new InfracostResourceCost(name, cost.Value));
            }
        }
    }

    private static decimal? ReadCost(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property))
        {
            return null;
        }

        var text = property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private static string? ReadString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return property.GetString();
    }

    private static string ExtractJson(string output)
    {
        var trimmed = output.Trim().TrimStart('\uFEFF');
        var start = trimmed.IndexOf('{');
        if (start < 0)
        {
            throw new JsonException("Infracost output did not contain JSON.");
        }

        return trimmed[start..];
    }
}

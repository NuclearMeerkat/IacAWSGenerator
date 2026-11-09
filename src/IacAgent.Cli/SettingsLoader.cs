using System.Globalization;

using IacAgent.Host.Configuration;

using Microsoft.Extensions.Configuration;

namespace IacAgent.Cli;

internal static class SettingsLoader
{
    public static SettingsOverlay LoadFile(string directory, ICollection<string> errors)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(directory)
            .AddJsonFile("iacagent.json", optional: true)
            .Build();
        return Read(configuration, "iacagent.json", errors);
    }

    public static SettingsOverlay LoadEnvironment(ICollection<string> errors)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["profile"] = Environment.GetEnvironmentVariable("IACAGENT_PROFILE"),
            ["outputDirectory"] = Environment.GetEnvironmentVariable("IACAGENT_OUT"),
            ["modelStrong"] = Environment.GetEnvironmentVariable("IACAGENT_MODEL_STRONG"),
            ["modelCheap"] = Environment.GetEnvironmentVariable("IACAGENT_MODEL_CHEAP"),
            ["endpoint"] = Environment.GetEnvironmentVariable("IACAGENT_ENDPOINT"),
            ["maxRetries"] = Environment.GetEnvironmentVariable("IACAGENT_MAX_RETRIES"),
            ["autoApprove"] = Environment.GetEnvironmentVariable("IACAGENT_AUTO_APPROVE"),
            ["verbose"] = Environment.GetEnvironmentVariable("IACAGENT_VERBOSE"),
            ["budget"] = Environment.GetEnvironmentVariable("IACAGENT_BUDGET"),
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return Read(configuration, "environment", errors);
    }

    public static bool HasOpenRouterApiKey()
    {
        return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENROUTER_API_KEY"));
    }

    private static SettingsOverlay Read(IConfiguration configuration, string source, ICollection<string> errors)
    {
        return new SettingsOverlay
        {
            Profile = BlankToNull(configuration["profile"]),
            OutputDirectory = BlankToNull(configuration["outputDirectory"]),
            ModelStrong = BlankToNull(configuration["modelStrong"]),
            ModelCheap = BlankToNull(configuration["modelCheap"]),
            Endpoint = BlankToNull(configuration["endpoint"]),
            Budget = BlankToNull(configuration["budget"]),
            MaxRepairIterations = ReadInt(configuration["maxRetries"], source, errors),
            AutoApprove = ReadBool(configuration["autoApprove"], source, errors),
            Verbose = ReadBool(configuration["verbose"], source, errors),
        };
    }

    private static int? ReadInt(string? text, string source, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        errors.Add($"maxRetries in {source} must be an integer.");
        return null;
    }

    private static bool? ReadBool(string? text, string source, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (bool.TryParse(text, out var value))
        {
            return value;
        }

        errors.Add($"A boolean in {source} must be true or false.");
        return null;
    }

    private static string? BlankToNull(string? text)
    {
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }
}

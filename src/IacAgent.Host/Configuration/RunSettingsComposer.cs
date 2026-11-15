using System.Globalization;

namespace IacAgent.Host.Configuration;

/// <summary>Applies command flags over environment variables over iacagent.json.</summary>
public static class RunSettingsComposer
{
    /// <summary>Combines the three layers. A null or blank value does not override a lower layer.</summary>
    public static RunSettings Compose(SettingsOverlay file, SettingsOverlay environment, SettingsOverlay command)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(command);
        var budgetText = FirstText(command.Budget, environment.Budget, file.Budget);
        decimal? budget = null;
        if (budgetText is not null && decimal.TryParse(budgetText, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            budget = parsed;
        }

        return new RunSettings
        {
            Profile = FirstText(command.Profile, environment.Profile, file.Profile) ?? "trio",
            OutputDirectory = FirstText(command.OutputDirectory, environment.OutputDirectory, file.OutputDirectory) ?? "./out",
            ModelStrong = FirstText(command.ModelStrong, environment.ModelStrong, file.ModelStrong),
            ModelCheap = FirstText(command.ModelCheap, environment.ModelCheap, file.ModelCheap),
            Endpoint = FirstText(command.Endpoint, environment.Endpoint, file.Endpoint),
            MaxRepairIterations = command.MaxRepairIterations ?? environment.MaxRepairIterations ?? file.MaxRepairIterations,
            MaxTotalTokens = command.MaxTotalTokens ?? environment.MaxTotalTokens ?? file.MaxTotalTokens,
            Security = NormalizeSecurity(FirstText(command.Security, environment.Security, file.Security)) ?? "baseline",
            AutoApprove = command.AutoApprove ?? environment.AutoApprove ?? file.AutoApprove ?? false,
            Verbose = command.Verbose ?? environment.Verbose ?? file.Verbose ?? false,
            BudgetText = budgetText,
            MonthlyBudgetUsd = budget,
        };
    }

    /// <summary>Returns a message when the settings cannot start a run.</summary>
    public static string? Validate(RunSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.MaxRepairIterations is < 0)
        {
            return "--max-retries must be zero or greater.";
        }

        if (settings.MaxTotalTokens is < 0)
        {
            return "--max-tokens must be zero or greater.";
        }

        if (!IsSecurity(settings.Security))
        {
            return "--security must be baseline or strict.";
        }

        if (string.IsNullOrWhiteSpace(settings.Profile))
        {
            return "A profile name is required.";
        }

        if (string.IsNullOrWhiteSpace(settings.OutputDirectory))
        {
            return "An output directory is required.";
        }

        if (settings.BudgetText is not null && settings.MonthlyBudgetUsd is null)
        {
            return "--budget must be a number of USD per month, such as 25 or 10.50.";
        }

        if (settings.MonthlyBudgetUsd is < 0)
        {
            return "--budget must be zero or greater.";
        }

        return null;
    }

    private static string? NormalizeSecurity(string? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value.Equals("baseline", StringComparison.OrdinalIgnoreCase))
        {
            return "baseline";
        }

        if (value.Equals("strict", StringComparison.OrdinalIgnoreCase))
        {
            return "strict";
        }

        return value;
    }

    private static bool IsSecurity(string value)
    {
        return value is "baseline" or "strict";
    }

    private static string? FirstText(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }
}

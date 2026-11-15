using System.CommandLine;
using System.Reflection;
using System.Text.Json;

using IacAgent.Core;
using IacAgent.Host;
using IacAgent.Host.Benchmark;
using IacAgent.Host.Configuration;
using IacAgent.Host.Pipeline;
using IacAgent.Host.Redaction;
using IacAgent.Host.Review;
using IacAgent.Plugins.Agents;
using IacAgent.Plugins.Gates;
using IacAgent.Plugins.Gates.Infracost;
using IacAgent.Plugins.Models;
using IacAgent.Plugins.Terraform;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using HostingHost = Microsoft.Extensions.Hosting.Host;

namespace IacAgent.Cli;

/// <summary>Composition root for iacagent.</summary>
public static class Application
{
    /// <summary>Parses arguments and runs doctor or generate.</summary>
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var root = CliCommands.Create(GenerateAsync, DoctorAsync, ReviewAsync, BenchAsync);
        var parsed = root.Parse(args);
        if (parsed.Errors.Count > 0)
        {
            await parsed.InvokeAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            return ExitCodes.ConfigError;
        }

        return await parsed.InvokeAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> GenerateAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        if (!TryCompose(CliCommands.ReadGenerate(parseResult), out var settings, out var error))
        {
            Console.Error.WriteLine(error);
            return ExitCodes.ConfigError;
        }

        var prompt = CliCommands.ReadPrompt(parseResult);
        if (string.IsNullOrWhiteSpace(prompt))
        {
            Console.Error.WriteLine("A prompt is required.");
            return ExitCodes.ConfigError;
        }

        using var host = CreateHost(settings);
        if (host is null)
        {
            return ExitCodes.ConfigError;
        }

        var runner = host.Services.GetRequiredService<PipelineRunner>();
        var result = await runner.RunAsync(
            new GenerateRequest(
                prompt,
                settings.Profile,
                settings.OutputDirectory,
                settings.ModelStrong,
                settings.ModelCheap,
                settings.Endpoint,
                settings.MaxRepairIterations,
                settings.MaxTotalTokens,
                settings.AutoApprove,
                settings.Verbose,
                settings.Security,
                CliVersion(),
                settings.Temperature),
            cancellationToken).ConfigureAwait(false);
        GenerateConsole.Write(result.Outcome, result.OutputDirectory);
        return ExitCodes.For(result.Outcome);
    }

    private static async Task<int> DoctorAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        if (!TryCompose(CliCommands.ReadDoctor(parseResult), out var settings, out var error))
        {
            Console.Error.WriteLine(error);
            return ExitCodes.ConfigError;
        }

        using var host = CreateHost(settings);
        if (host is null)
        {
            return ExitCodes.ConfigError;
        }

        var doctor = host.Services.GetRequiredService<IacAgent.Host.Doctor.Doctor>();
        var report = await doctor.CheckAsync(
            new IacAgent.Host.Doctor.DoctorRequest(SettingsLoader.HasOpenRouterApiKey(), settings.Endpoint),
            cancellationToken).ConfigureAwait(false);
        DoctorConsole.Write(report);
        return report.Succeeded ? ExitCodes.Success : ExitCodes.ConfigError;
    }

    private static async Task<int> ReviewAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        if (!TryCompose(CliCommands.ReadReview(parseResult), out var settings, out var error))
        {
            Console.Error.WriteLine(error);
            return ExitCodes.ConfigError;
        }

        var (planPath, sourceDirectory) = CliCommands.ReadReviewPaths(parseResult);
        if (string.IsNullOrWhiteSpace(planPath) || !File.Exists(planPath))
        {
            Console.Error.WriteLine("Pass --plan with an existing terraform show -json file.");
            return ExitCodes.ConfigError;
        }

        if (string.IsNullOrWhiteSpace(sourceDirectory))
        {
            sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(planPath)) ?? ".";
        }

        if (!Directory.Exists(sourceDirectory))
        {
            Console.Error.WriteLine("The Terraform source directory does not exist.");
            return ExitCodes.ConfigError;
        }

        using var host = CreateHost(settings);
        if (host is null)
        {
            return ExitCodes.ConfigError;
        }

        var reviewer = host.Services.GetRequiredService<PlanReviewer>();
        try
        {
            var review = await reviewer.ReviewAsync(planPath, sourceDirectory, cancellationToken).ConfigureAwait(false);
            ReviewConsole.Write(review);
            return ExitCodes.Success;
        }
        catch (JsonException)
        {
            Console.Error.WriteLine("The plan file is not JSON.");
            return ExitCodes.ConfigError;
        }
        catch (InvalidOperationException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return ExitCodes.ConfigError;
        }
    }

    private static async Task<int> BenchAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        var (suitePath, resume, endpoint) = CliCommands.ReadBench(parseResult);
        if (string.IsNullOrWhiteSpace(suitePath) || !File.Exists(suitePath))
        {
            Console.Error.WriteLine("Pass --suite with an existing suite file.");
            return ExitCodes.ConfigError;
        }

        if (!TryCompose(new SettingsOverlay { Endpoint = endpoint }, out var settings, out var error))
        {
            Console.Error.WriteLine(error);
            return ExitCodes.ConfigError;
        }

        try
        {
            var root = await BenchmarkCommand.RunAsync(
                await File.ReadAllTextAsync(suitePath, cancellationToken).ConfigureAwait(false),
                resume,
                settings.Endpoint,
                settings.MaxRepairIterations,
                settings.MaxTotalTokens,
                settings.Verbose,
                CliVersion(),
                RunBenchCaseAsync,
                cancellationToken).ConfigureAwait(false);
            Console.WriteLine(root);
            return ExitCodes.Success;
        }
        catch (InvalidOperationException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return ExitCodes.ConfigError;
        }
    }

    private static async Task RunBenchCaseAsync(GenerateRequest request, decimal? budget, CancellationToken cancellationToken)
    {
        var settings = new RunSettings
        {
            Profile = request.Profile,
            OutputDirectory = request.OutputDirectory,
            ModelStrong = request.ModelStrong,
            ModelCheap = request.ModelCheap,
            Endpoint = request.Endpoint,
            Security = request.Security,
            Temperature = request.Temperature,
            AutoApprove = true,
            ToolVersion = request.ToolVersion,
            MonthlyBudgetUsd = budget,
        };
        using var host = CreateHost(settings);
        if (host is null)
        {
            throw new InvalidOperationException("The benchmark host could not be created.");
        }

        await host.Services.GetRequiredService<PipelineRunner>().RunAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private static bool TryCompose(SettingsOverlay command, out RunSettings settings, out string? error)
    {
        var errors = new List<string>();
        var file = SettingsLoader.LoadFile(Directory.GetCurrentDirectory(), errors);
        var environment = SettingsLoader.LoadEnvironment(errors);
        if (errors.Count > 0)
        {
            settings = null!;
            error = string.Join(Environment.NewLine, errors);
            return false;
        }

        settings = RunSettingsComposer.Compose(file, environment, command);
        error = RunSettingsComposer.Validate(settings);
        return error is null;
    }

    private static IHost? CreateHost(RunSettings settings)
    {
        try
        {
            return BuildHost(settings);
        }
        catch (InvalidOperationException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return null;
        }
    }

    private static IHost BuildHost(RunSettings settings)
    {
        var builder = HostingHost.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ApplicationName = "iacagent",
            ContentRootPath = Directory.GetCurrentDirectory(),
            Args = [],
        });
        builder.Logging.SetMinimumLevel(settings.Verbose ? LogLevel.Debug : LogLevel.Information);
        builder.Configuration.AddJsonFile("iacagent.json", optional: true, reloadOnChange: false);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Checkov:Level"] = settings.Security,
        });
        builder.Services.AddSingleton(new ModelConnection(settings.ModelStrong, settings.ModelCheap, settings.Endpoint, Temperature(settings.Temperature)));
        builder.Services.AddSingleton<IUserInteraction, ConsoleUserInteraction>();
        builder.Services.AddSingleton<IRedactionRule, AwsAccessKeyRedactionRule>();
        builder.Services.AddSingleton<IRedactionRule, OpenRouterKeyRedactionRule>();
        builder.Services.AddSingleton<IRedactionRule, AssignedSecretRedactionRule>();
        builder.Services.AddSingleton<IRedactionRule, JsonSecretRedactionRule>();
        builder.Services.AddHost(builder.Configuration);
        builder.Services.AddAgents(builder.Configuration);
        builder.Services.AddModels(builder.Configuration);
        builder.Services.AddTerraform(builder.Configuration);
        builder.Services.AddSingleton(new BudgetLimit(settings.MonthlyBudgetUsd));
        builder.Services.AddGates(builder.Configuration);
        return builder.Build();
    }

    private static string CliVersion()
    {
        var version = typeof(Application).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return version ?? string.Empty;
    }

    private static float? Temperature(string value)
    {
        if (value.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return float.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
    }
}

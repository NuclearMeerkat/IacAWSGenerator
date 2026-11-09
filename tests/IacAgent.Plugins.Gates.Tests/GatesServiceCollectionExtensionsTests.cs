using IacAgent.Core;
using IacAgent.Plugins.Gates;
using IacAgent.Plugins.Gates.Infracost;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IacAgent.Plugins.Gates.Tests;

public sealed class GatesServiceCollectionExtensionsTests
{
    [Fact]
    public void AddGates_RegistersValidateCheckovThenInfracost()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IProcessRunner, IdleProcessRunner>();
        services.AddSingleton(new BudgetLimit(null));
        services.AddGates(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();

        var names = provider.GetServices<IGate>().OrderBy(gate => gate.Order).Select(gate => gate.Name).ToArray();

        Assert.Equal(["validate", "checkov", "infracost"], names);
    }

    [Fact]
    public async Task CheckovGate_HighFinding_FailsAndKeepsLowFinding()
    {
        const string json = """
            {
              "results": {
                "failed_checks": [
                  {"check_id":"CKV_AWS_24","check_name":"open ssh","file_path":"/main.tf","file_line_range":[4,10],"severity":"HIGH"},
                  {"check_id":"CKV_AWS_1","check_name":"tag","file_path":"/main.tf","file_line_range":[1,2],"severity":"LOW"}
                ]
              }
            }
            """;
        var gate = new ServiceCollection()
            .AddSingleton<IProcessRunner>(new ScriptedProcessRunner(json))
            .AddSingleton(new BudgetLimit(null))
            .AddGates(new ConfigurationBuilder().Build())
            .BuildServiceProvider()
            .GetServices<IGate>()
            .Single(candidate => candidate.Name == "checkov");

        var result = await gate.CheckAsync(new Artifact(Path.GetTempPath(), []), CancellationToken.None);

        Assert.False(result.Passed);
        Assert.Equal(2, result.Issues.Count);
        Assert.Equal("CKV_AWS_24", result.Issues[0].RuleId);
    }

    [Fact]
    public async Task CheckovGate_UnratedFinding_DoesNotFail()
    {
        const string json = """
            {"results":{"failed_checks":[{"check_id":"CKV2_AWS_5","check_name":"security group attached","file_path":"/main.tf","file_line_range":[1,4]}]}}
            """;
        var gate = new ServiceCollection()
            .AddSingleton<IProcessRunner>(new ScriptedProcessRunner(json))
            .AddSingleton(new BudgetLimit(null))
            .AddGates(new ConfigurationBuilder().Build())
            .BuildServiceProvider()
            .GetServices<IGate>()
            .Single(candidate => candidate.Name == "checkov");

        var result = await gate.CheckAsync(new Artifact(Path.GetTempPath(), []), CancellationToken.None);

        Assert.True(result.Passed);
        Assert.Equal("UNKNOWN", Assert.Single(result.Issues).Severity);
    }

    [Fact]
    public async Task CheckovGate_UnratedOpenSsh_Fails()
    {
        const string json = """
            {"results":{"failed_checks":[{"check_id":"CKV_AWS_24","check_name":"open ssh","file_path":"/main.tf","file_line_range":[4,10]}]}}
            """;
        var gate = new ServiceCollection()
            .AddSingleton<IProcessRunner>(new ScriptedProcessRunner(json))
            .AddSingleton(new BudgetLimit(null))
            .AddGates(new ConfigurationBuilder().Build())
            .BuildServiceProvider()
            .GetServices<IGate>()
            .Single(candidate => candidate.Name == "checkov");

        var result = await gate.CheckAsync(new Artifact(Path.GetTempPath(), []), CancellationToken.None);

        Assert.False(result.Passed);
        Assert.Equal("CKV_AWS_24", Assert.Single(result.Issues).RuleId);
    }

    [Fact]
    public async Task ValidateGate_InvalidJsonDiagnostics_ReturnsIssue()
    {
        const string json = """
            {"diagnostics":[{"severity":"error","summary":"Missing expression","detail":"Expected an expression.","range":{"filename":"main.tf","start":{"line":2}}}]}
            """;
        var gate = new ServiceCollection()
            .AddSingleton<IProcessRunner>(new TerraformProcessRunner(json))
            .AddSingleton(new BudgetLimit(null))
            .AddGates(new ConfigurationBuilder().Build())
            .BuildServiceProvider()
            .GetServices<IGate>()
            .Single(candidate => candidate.Name == "validate");

        var result = await gate.CheckAsync(new Artifact(Path.GetTempPath(), []), CancellationToken.None);

        Assert.False(result.Passed);
        var issue = Assert.Single(result.Issues);
        Assert.Equal(2, issue.Line);
        Assert.Equal("main.tf", issue.File);
    }

    [Fact]
    public async Task InfracostGate_OverBudget_FailsWithReadableMessage()
    {
        const string json = """
            {
              "currency": "USD",
              "summary": { "total_monthly_cost": "42.50" },
              "projects": [
                {
                  "resources": [
                    { "name": "aws_nat_gateway.main", "cost_components": [ { "total_monthly_cost": "32.85" } ] },
                    { "name": "aws_lambda_function.hello", "cost_components": [ { "total_monthly_cost": "0.20" } ] }
                  ]
                }
              ]
            }
            """;
        var gate = Gate(json, 10m);

        var result = await gate.CheckAsync(new Artifact(Path.GetTempPath(), []), CancellationToken.None);

        Assert.False(result.Passed);
        var issue = Assert.Single(result.Issues);
        Assert.Equal(KnownGateRules.BudgetExceeded, issue.RuleId);
        Assert.Contains("42.50 USD", issue.Message, StringComparison.Ordinal);
        Assert.Contains("10.00 USD", issue.Message, StringComparison.Ordinal);
        Assert.Contains("aws_nat_gateway.main", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InfracostGate_UnderBudget_Passes()
    {
        var gate = Gate("""{"currency":"USD","totalMonthlyCost":"1.25","projects":[]}""", 10m);

        var result = await gate.CheckAsync(new Artifact(Path.GetTempPath(), []), CancellationToken.None);

        Assert.True(result.Passed);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public async Task InfracostGate_NoBudget_DoesNotRunTheTool()
    {
        var gate = new ServiceCollection()
            .AddSingleton<IProcessRunner>(new ThrowingProcessRunner())
            .AddSingleton(new BudgetLimit(null))
            .AddGates(new ConfigurationBuilder().Build())
            .BuildServiceProvider()
            .GetServices<IGate>()
            .Single(candidate => candidate.Name == "infracost");

        var result = await gate.CheckAsync(new Artifact(Path.GetTempPath(), []), CancellationToken.None);

        Assert.True(result.Passed);
    }

    private static IGate Gate(string json, decimal budget)
    {
        return new ServiceCollection()
            .AddSingleton<IProcessRunner>(new ScriptedProcessRunner(json))
            .AddSingleton(new BudgetLimit(budget))
            .AddGates(new ConfigurationBuilder().Build())
            .BuildServiceProvider()
            .GetServices<IGate>()
            .Single(candidate => candidate.Name == "infracost");
    }

    private sealed class ThrowingProcessRunner : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Infracost was called.");
        }
    }

    private sealed class IdleProcessRunner : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory, CancellationToken cancellationToken)
        {
            return Task.FromResult(new ProcessResult(0, string.Empty, string.Empty));
        }
    }

    private sealed class ScriptedProcessRunner(string stdout) : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory, CancellationToken cancellationToken)
        {
            return Task.FromResult(new ProcessResult(1, stdout, string.Empty));
        }
    }

    private sealed class TerraformProcessRunner(string validateJson) : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory, CancellationToken cancellationToken)
        {
            var stdout = arguments.Contains("validate") ? validateJson : string.Empty;
            return Task.FromResult(new ProcessResult(arguments.Contains("validate") ? 1 : 0, stdout, string.Empty));
        }
    }
}

using IacAgent.Core;
using IacAgent.Plugins.Gates;
using IacAgent.Plugins.Gates.Checkov;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IacAgent.Plugins.Gates.Tests.Checkov;

public sealed class CheckovGateTests
{
    [Fact]
    public async Task CheckAsync_HighFinding_FailsAndKeepsTheOtherFinding()
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
        var result = await RunAsync(json, new ConfigurationBuilder().Build());

        Assert.False(result.Passed);
        Assert.Equal(2, result.Issues.Count);
        Assert.Equal("CKV_AWS_24", result.Issues[0].RuleId);
    }

    [Fact]
    public async Task CheckAsync_UnratedFinding_DoesNotFail()
    {
        const string json = """
            {"results":{"failed_checks":[{"check_id":"CKV2_AWS_5","check_name":"security group attached","file_path":"/main.tf","file_line_range":[1,4]}]}}
            """;
        var result = await RunAsync(json, new ConfigurationBuilder().Build());

        Assert.True(result.Passed);
        Assert.Equal("UNKNOWN", Assert.Single(result.Issues).Severity);
    }

    [Fact]
    public async Task CheckAsync_UnratedOpenSsh_Fails()
    {
        const string json = """
            {"results":{"failed_checks":[{"check_id":"CKV_AWS_24","check_name":"open ssh","file_path":"/main.tf","file_line_range":[4,10]}]}}
            """;
        var result = await RunAsync(json, new ConfigurationBuilder().Build());

        Assert.False(result.Passed);
        Assert.Equal("CKV_AWS_24", Assert.Single(result.Issues).RuleId);
    }

    [Fact]
    public async Task CheckAsync_SavedOutput_BaselineBlocksListedCheckOnly()
    {
        var json = await File.ReadAllTextAsync(Sample("checkov-security-group.json"));
        var result = await RunAsync(json, new ConfigurationBuilder().Build());

        Assert.False(result.Passed);
        Assert.True(result.Issues.Single(issue => issue.RuleId == "CKV_AWS_24").IsBlocking);
        Assert.False(result.Issues.Single(issue => issue.RuleId == "CKV2_AWS_5").IsBlocking);
    }

    [Fact]
    public async Task CheckAsync_Strict_FailsUnlistedCheck()
    {
        const string json = """
            {"results":{"failed_checks":[{"check_id":"CKV2_AWS_5","check_name":"unattached","file_path":"/main.tf","file_line_range":[1,2]}]}}
            """;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Checkov:Level"] = "strict" }).Build();
        var result = await RunAsync(json, configuration);

        Assert.False(result.Passed);
        Assert.True(Assert.Single(result.Issues).IsBlocking);
    }

    [Fact]
    public async Task CheckAsync_StrictSkip_PassesListedSkip()
    {
        const string json = """
            {"results":{"failed_checks":[{"check_id":"CKV_AWS_24","check_name":"open ssh","file_path":"/main.tf","file_line_range":[1,2]}]}}
            """;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Checkov:Level"] = "strict",
            ["Checkov:StrictSkipChecks:0"] = "CKV_AWS_24",
        }).Build();
        var result = await RunAsync(json, configuration);

        Assert.True(result.Passed);
        Assert.False(Assert.Single(result.Issues).IsBlocking);
    }

    [Fact]
    public async Task CheckAsync_SkippedBlockingCheck_FailsWithSuppressionMessage()
    {
        const string json = """
            {"results":{"skipped_checks":[{"check_id":"CKV_AWS_24","check_name":"open ssh","file_path":"/main.tf","file_line_range":[1,4]}]}}
            """;
        var result = await RunAsync(json, new ConfigurationBuilder().Build());

        Assert.False(result.Passed);
        var issue = Assert.Single(result.Issues);
        Assert.Equal("Suppressed with checkov:skip. Remove the suppression and fix the resource.", issue.Message);
    }

    [Fact]
    public void AddGates_UnknownLevel_Throws()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Checkov:Level"] = "paranoid" }).Build();

        var exception = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddGates(configuration));

        Assert.Contains("baseline or strict", exception.Message, StringComparison.Ordinal);
    }

    private static async Task<GateResult> RunAsync(string json, IConfiguration configuration)
    {
        var gate = new CheckovGate(new ScriptedProcessRunner(json), CheckovOptions.Bind(configuration));
        return await gate.CheckAsync(new Artifact(Path.GetTempPath(), []), CancellationToken.None);
    }

    private static string Sample(string name)
    {
        return Path.Combine(AppContext.BaseDirectory, "Samples", name);
    }
}

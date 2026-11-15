using System.Text.Json;

using IacAgent.Core;
using IacAgent.Host;
using IacAgent.Host.Configuration;
using IacAgent.Host.Pipeline;
using IacAgent.Host.Redaction;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IacAgent.Host.Tests.Pipeline;

public sealed class RepairLoopTests
{
    [Fact]
    public async Task RunAsync_RepairLimit_WritesEscalation()
    {
        var directory = Directory.CreateTempSubdirectory("iacagent-").FullName;
        try
        {
            var runner = RepairLoopFixture.CreateRunner(new SwitchingDeveloper(), new ContentFailGate(), maxTokens: null);
            var result = await runner.RunAsync(RepairLoopFixture.Request(directory, maxRepairs: 1), CancellationToken.None);

            Assert.Equal(RunOutcome.GateFailed, result.Outcome);
            Assert.Contains("# 2", await File.ReadAllTextAsync(Path.Combine(directory, "escalation.md")), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_SameIssuesTwice_StopsEarly()
    {
        var directory = Directory.CreateTempSubdirectory("iacagent-").FullName;
        try
        {
            var developer = new CountingDeveloper();
            var runner = RepairLoopFixture.CreateRunner(developer, new AlwaysFailGate());
            await runner.RunAsync(RepairLoopFixture.Request(directory), CancellationToken.None);

            Assert.Equal(2, developer.Calls);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_OverBudget_ReturnsBudgetExceeded()
    {
        var directory = Directory.CreateTempSubdirectory("iacagent-").FullName;
        try
        {
            var runner = RepairLoopFixture.CreateRunner(new SwitchingDeveloper(), new BudgetGate());
            var result = await runner.RunAsync(RepairLoopFixture.Request(directory, maxRepairs: 0), CancellationToken.None);
            var report = await File.ReadAllTextAsync(Path.Combine(directory, "escalation.md"));

            Assert.Equal(RunOutcome.BudgetExceeded, result.Outcome);
            Assert.Contains("42.50 USD", report, StringComparison.Ordinal);
            Assert.Contains("10.00 USD", report, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_TokenLimit_ReturnsGateFailed()
    {
        var directory = Directory.CreateTempSubdirectory("iacagent-").FullName;
        try
        {
            var runner = RepairLoopFixture.CreateRunner(new SwitchingDeveloper(), new ContentFailGate(), maxTokens: 1);
            var result = await runner.RunAsync(RepairLoopFixture.Request(directory), CancellationToken.None);
            var report = await File.ReadAllTextAsync(Path.Combine(directory, "escalation.md"));

            Assert.Equal(RunOutcome.GateFailed, result.Outcome);
            Assert.Contains("Outcome: GateFailed", report, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_FakeProfileAndGate_RunThroughHost()
    {
        var directory = Directory.CreateTempSubdirectory("iacagent-").FullName;
        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IModelRouter>(new IdleRouter());
            services.AddSingleton<IProcessRunner, QuietProcessRunner>();
            services.AddHost(new ConfigurationBuilder().Build());
            services.AddSingleton<IProfileDefinition>(new HostedProfile());
            services.AddSingleton<IGate, PassingGate>();
            using var provider = services.BuildServiceProvider();
            var runner = provider.GetRequiredService<PipelineRunner>();

            var result = await runner.RunAsync(RepairLoopFixture.Request(directory, "hosted"), CancellationToken.None);

            Assert.Equal(RunOutcome.Succeeded, result.Outcome);
            Assert.Contains("resource", await File.ReadAllTextAsync(Path.Combine(directory, "main.tf")), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_EmptyRepair_KeepsOriginalGateIssues()
    {
        var directory = Directory.CreateTempSubdirectory("iacagent-").FullName;
        try
        {
            var developer = new DroppingDeveloper();
            var runner = RepairLoopFixture.CreateRunner(developer, new AlwaysFailGate());
            await runner.RunAsync(RepairLoopFixture.Request(directory, maxRepairs: 2), CancellationToken.None);
            var rules = developer.Seen[2]!.SelectMany(result => result.Issues).Select(issue => issue.RuleId).ToArray();

            Assert.Contains("same", rules);
            Assert.Contains("developer-output", rules);
            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "run.json")));
            var iterations = json.RootElement.GetProperty("iterations");
            Assert.Equal(1, iterations[1].GetProperty("gates").GetArrayLength());
            Assert.Equal("developer", iterations[1].GetProperty("gates")[0].GetProperty("name").GetString());
            Assert.Equal(1, iterations[1].GetProperty("blockingIssues").GetInt32());
            Assert.Equal(1, iterations[2].GetProperty("gates").GetArrayLength());
            Assert.Equal(1, iterations[2].GetProperty("blockingIssues").GetInt32());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_SameRuleOnAnotherLine_StopsAsRepeated()
    {
        var directory = Directory.CreateTempSubdirectory("iacagent-").FullName;
        try
        {
            var developer = new CountingDeveloper();
            var runner = RepairLoopFixture.CreateRunner(developer, new MovingLineGate());
            await runner.RunAsync(RepairLoopFixture.Request(directory), CancellationToken.None);

            Assert.Equal(2, developer.Calls);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_MaxRetries_OverridesProfile()
    {
        var directory = Directory.CreateTempSubdirectory("iacagent-").FullName;
        try
        {
            var developer = new CountingDeveloper();
            var runner = RepairLoopFixture.CreateRunner(developer, new AlwaysFailGate(), profileRepairs: 5);
            await runner.RunAsync(RepairLoopFixture.Request(directory, maxRepairs: 0), CancellationToken.None);

            Assert.Equal(1, developer.Calls);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_UnsetRetries_UsesProfileLimit()
    {
        var directory = Directory.CreateTempSubdirectory("iacagent-").FullName;
        try
        {
            var developer = new CountingDeveloper();
            var runner = RepairLoopFixture.CreateRunner(developer, new AlwaysFailGate(), profileRepairs: 0);
            await runner.RunAsync(RepairLoopFixture.Request(directory, maxRepairs: null), CancellationToken.None);

            Assert.Equal(1, developer.Calls);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_MaxTokens_StopsTheLoop()
    {
        var directory = Directory.CreateTempSubdirectory("iacagent-").FullName;
        try
        {
            var runner = RepairLoopFixture.CreateRunner(new SwitchingDeveloper(), new AlwaysFailGate(), maxTokens: null);
            var result = await runner.RunAsync(RepairLoopFixture.Request(directory, maxTokens: 0), CancellationToken.None);
            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "run.json")));

            Assert.Equal(RunOutcome.GateFailed, result.Outcome);
            Assert.Single(json.RootElement.GetProperty("iterations").EnumerateArray());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_AdvisoryOnly_Succeeds()
    {
        var directory = Directory.CreateTempSubdirectory("iacagent-").FullName;
        try
        {
            var runner = RepairLoopFixture.CreateRunner(new SwitchingDeveloper(), new AdvisoryGate());
            var result = await runner.RunAsync(RepairLoopFixture.Request(directory, maxRepairs: 0), CancellationToken.None);

            Assert.Equal(RunOutcome.Succeeded, result.Outcome);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_AdvisoryNoise_DoesNotStopTheRepeatCheck()
    {
        var directory = Directory.CreateTempSubdirectory("iacagent-").FullName;
        try
        {
            var developer = new CountingDeveloper();
            var runner = RepairLoopFixture.CreateRunner(developer, new NoisyBlockingGate());
            await runner.RunAsync(RepairLoopFixture.Request(directory), CancellationToken.None);

            Assert.Equal(2, developer.Calls);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_FormattedFile_IsWhatTheNextRepairReceives()
    {
        var directory = Directory.CreateTempSubdirectory("iacagent-").FullName;
        try
        {
            var developer = new ContentRecordingDeveloper();
            var runner = RepairLoopFixture.CreateRunner(developer, new RewritingGate());
            await runner.RunAsync(RepairLoopFixture.Request(directory, maxRepairs: 1), CancellationToken.None);

            Assert.Equal("formatted\n", developer.RepairContent);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_Repair_KeepsFilesTheDeveloperDidNotReturn()
    {
        var directory = Directory.CreateTempSubdirectory("iacagent-").FullName;
        try
        {
            var runner = RepairLoopFixture.CreateRunner(new PartialRepairDeveloper(), new MainOnlyGate(), maxTokens: null);
            var result = await runner.RunAsync(RepairLoopFixture.Request(directory, maxRepairs: 1), CancellationToken.None);

            Assert.Equal(RunOutcome.Succeeded, result.Outcome);
            Assert.Contains("variable", await File.ReadAllTextAsync(Path.Combine(directory, "variables.tf")), StringComparison.Ordinal);
            Assert.Contains("good", await File.ReadAllTextAsync(Path.Combine(directory, "main.tf")), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_Reviewer_AddsTokensToRunJson()
    {
        var directory = Directory.CreateTempSubdirectory("iacagent-").FullName;
        try
        {
            var runner = RepairLoopFixture.CreateRunner(new SwitchingDeveloper(), new LowIssueGate(), reviewer: new TokenReviewer());
            await runner.RunAsync(RepairLoopFixture.Request(directory, maxRepairs: 0), CancellationToken.None);
            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "run.json")));

            Assert.Equal(6, json.RootElement.GetProperty("tokens").GetProperty("input").GetInt32());
            Assert.Equal(8, json.RootElement.GetProperty("tokens").GetProperty("output").GetInt32());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_OrchestratorFailed_StopsBeforeDeveloper()
    {
        var directory = Directory.CreateTempSubdirectory("iacagent-").FullName;
        try
        {
            var developer = new CountingDeveloper();
            var runner = RepairLoopFixture.CreateRunner(developer, new PassingGate(), orchestrator: new FailedOrchestrator());
            var result = await runner.RunAsync(RepairLoopFixture.Request(directory), CancellationToken.None);
            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "run.json")));

            Assert.Equal(RunOutcome.GateFailed, result.Outcome);
            Assert.Equal(0, developer.Calls);
            Assert.False(File.Exists(Path.Combine(directory, "main.tf")));
            Assert.Equal(3, json.RootElement.GetProperty("tokens").GetProperty("input").GetInt32());
            Assert.Contains("did not return JSON", json.RootElement.GetProperty("assumptions")[0].GetString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_AdvisorReview_StaysOutOfRequirements()
    {
        var directory = Directory.CreateTempSubdirectory("iacagent-").FullName;
        try
        {
            var developer = new ContextDeveloper();
            var runner = RepairLoopFixture.CreateRunner(
                developer,
                new PassingGate(),
                orchestrator: new JsonOrchestrator(),
                advisor: new NoteAdvisor("Use one bucket."));
            await runner.RunAsync(RepairLoopFixture.Request(directory, maxRepairs: 0), CancellationToken.None);

            var context = Assert.Single(developer.Seen);
            Assert.Equal("""{"summary":"api"}""", context.RequirementsJson);
            Assert.Equal("Use one bucket.", context.DesignNotes);
            Assert.Null(context.OtherFiles);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_Repair_ListsOtherFilesWithoutTheirContent()
    {
        var directory = Directory.CreateTempSubdirectory("iacagent-").FullName;
        try
        {
            var developer = new TwoFileDeveloper();
            var runner = RepairLoopFixture.CreateRunner(developer, new MainFileGate());
            await runner.RunAsync(RepairLoopFixture.Request(directory, maxRepairs: 1), CancellationToken.None);

            var repair = developer.Seen[1];
            var line = Assert.Single(repair.OtherFiles!);
            Assert.Equal("variables.tf: aws_s3_bucket.data", line);
            Assert.DoesNotContain(repair.Artifact!.Files, file => file.RelativePath == "variables.tf");
            Assert.DoesNotContain("keep-me-out", repair.Artifact.Files[0].Content, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_Secrets_AreRedactedFromRunJsonAndEscalation()
    {
        var directory = Directory.CreateTempSubdirectory("iacagent-").FullName;
        try
        {
            var runner = RepairLoopFixture.CreateRunner(
                new SecretFileDeveloper(),
                new ContentFailGate(),
                rules: [new AwsAccessKeyRedactionRule()]);
            await runner.RunAsync(RepairLoopFixture.Request(directory, maxRepairs: 0) with { Prompt = "key AKIAIOSFODNN7EXAMPLE" }, CancellationToken.None);
            var json = await File.ReadAllTextAsync(Path.Combine(directory, "run.json"));
            var report = await File.ReadAllTextAsync(Path.Combine(directory, "escalation.md"));

            Assert.DoesNotContain("AKIAIOSFODNN7EXAMPLE", json, StringComparison.Ordinal);
            Assert.DoesNotContain("AKIAIOSFODNN7EXAMPLE", report, StringComparison.Ordinal);
            Assert.Contains("## Last attempt", report, StringComparison.Ordinal);
            Assert.Contains("[redacted]", json, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

}

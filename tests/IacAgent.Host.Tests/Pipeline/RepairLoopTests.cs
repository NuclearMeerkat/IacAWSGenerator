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
            var runner = CreateRunner(new SwitchingDeveloper(), new ContentFailGate(), maxTokens: null);
            var result = await runner.RunAsync(Request(directory, maxRepairs: 1), CancellationToken.None);

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
            var runner = CreateRunner(developer, new AlwaysFailGate());
            await runner.RunAsync(Request(directory), CancellationToken.None);

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
            var runner = CreateRunner(new SwitchingDeveloper(), new BudgetGate());
            var result = await runner.RunAsync(Request(directory, maxRepairs: 0), CancellationToken.None);
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
            var runner = CreateRunner(new SwitchingDeveloper(), new ContentFailGate(), maxTokens: 1);
            var result = await runner.RunAsync(Request(directory), CancellationToken.None);
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
            services.AddHost(new ConfigurationBuilder().Build());
            services.AddSingleton<IProfileDefinition>(new HostedProfile());
            services.AddSingleton<IGate, PassingGate>();
            using var provider = services.BuildServiceProvider();
            var runner = provider.GetRequiredService<PipelineRunner>();

            var result = await runner.RunAsync(Request(directory, "hosted"), CancellationToken.None);

            Assert.Equal(RunOutcome.Succeeded, result.Outcome);
            Assert.Contains("resource", await File.ReadAllTextAsync(Path.Combine(directory, "main.tf")), StringComparison.Ordinal);
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
            var runner = CreateRunner(new PartialRepairDeveloper(), new MainOnlyGate(), maxTokens: null);
            var result = await runner.RunAsync(Request(directory, maxRepairs: 1), CancellationToken.None);

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
            var runner = CreateRunner(new SwitchingDeveloper(), new LowIssueGate(), reviewer: new TokenReviewer());
            await runner.RunAsync(Request(directory, maxRepairs: 0), CancellationToken.None);
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
            var runner = CreateRunner(developer, new PassingGate(), orchestrator: new FailedOrchestrator());
            var result = await runner.RunAsync(Request(directory), CancellationToken.None);
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
    public async Task RunAsync_Secrets_AreRedactedFromRunJsonAndEscalation()
    {
        var directory = Directory.CreateTempSubdirectory("iacagent-").FullName;
        try
        {
            var runner = CreateRunner(
                new SecretFileDeveloper(),
                new ContentFailGate(),
                rules: [new AwsAccessKeyRedactionRule()]);
            await runner.RunAsync(Request(directory, maxRepairs: 0) with { Prompt = "key AKIAIOSFODNN7EXAMPLE" }, CancellationToken.None);
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

    private static PipelineRunner CreateRunner(
        IAgent developer,
        IGate gate,
        int? maxTokens = null,
        IEnumerable<IRedactionRule>? rules = null,
        IModelRouter? router = null,
        IAgent? reviewer = null,
        IAgent? orchestrator = null)
    {
        var agents = new List<IAgent>();
        if (orchestrator is not null)
        {
            agents.Add(orchestrator);
        }

        agents.Add(developer);
        if (reviewer is not null)
        {
            agents.Add(reviewer);
        }

        var services = new ServiceCollection().BuildServiceProvider();
        var catalog = new ProfileCatalog([new ScriptedProfile(agents, maxTokens)]);
        return new PipelineRunner(catalog, [gate], services, NullLogger<PipelineRunner>.Instance, router, rules);
    }

    private static GenerateRequest Request(string directory, string profile = "scripted", int maxRepairs = 3)
    {
        return new GenerateRequest("hello", profile, directory, "test-model", null, null, maxRepairs, true);
    }

    private sealed class ScriptedProfile(IReadOnlyList<IAgent> agents, int? maxTokens) : IProfileDefinition
    {
        public string Name => "scripted";

        public PipelineProfile Build(IServiceProvider services)
        {
            return new PipelineProfile(Name, agents, new LoopPolicy(3, maxTokens));
        }
    }

    private sealed class HostedProfile : IProfileDefinition
    {
        public string Name => "hosted";

        public PipelineProfile Build(IServiceProvider services)
        {
            return new PipelineProfile(Name, [new FileDeveloper()], new LoopPolicy());
        }
    }

    private sealed class FileDeveloper : IAgent
    {
        public string Name => "file-developer";

        public AgentRole Role => AgentRole.Developer;

        public Task<AgentResult> RunAsync(AgentContext context, CancellationToken cancellationToken)
        {
            return Task.FromResult(new AgentResult([], Files: [new GeneratedFile("main.tf", "resource \"aws_s3_bucket\" \"logs\" {}\n")]));
        }
    }

    private sealed class SwitchingDeveloper : IAgent
    {
        private int _calls;

        public string Name => "switching";

        public AgentRole Role => AgentRole.Developer;

        public Task<AgentResult> RunAsync(AgentContext context, CancellationToken cancellationToken)
        {
            _calls++;
            return Task.FromResult(new AgentResult([], Files: [new GeneratedFile("main.tf", $"# {_calls}\n")], Tokens: new TokenCount(1, 1)));
        }
    }

    private sealed class CountingDeveloper : IAgent
    {
        public int Calls { get; private set; }

        public string Name => "counting";

        public AgentRole Role => AgentRole.Developer;

        public Task<AgentResult> RunAsync(AgentContext context, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new AgentResult([], Files: [new GeneratedFile("main.tf", "same\n")]));
        }
    }

    private sealed class AlwaysFailGate : IGate
    {
        public string Name => "always";

        public int Order => 1;

        public Task<GateResult> CheckAsync(Artifact artifact, CancellationToken cancellationToken)
        {
            return Task.FromResult(new GateResult(Name, false, [new GateIssue("main.tf", 1, "same", "HIGH", "same")]));
        }
    }

    private sealed class BudgetGate : IGate
    {
        public string Name => "infracost";

        public int Order => 30;

        public Task<GateResult> CheckAsync(Artifact artifact, CancellationToken cancellationToken)
        {
            return Task.FromResult(new GateResult(
                Name,
                false,
                [new GateIssue(string.Empty, null, KnownGateRules.BudgetExceeded, "error", "Estimated monthly cost is 42.50 USD. Budget is 10.00 USD.")]));
        }
    }

    private sealed class ContentFailGate : IGate
    {
        public string Name => "content";

        public int Order => 1;

        public Task<GateResult> CheckAsync(Artifact artifact, CancellationToken cancellationToken)
        {
            var content = artifact.Files.FirstOrDefault()?.Content ?? string.Empty;
            return Task.FromResult(new GateResult(Name, false, [new GateIssue("main.tf", 1, "rule", "HIGH", content)]));
        }
    }

    private sealed class PartialRepairDeveloper : IAgent
    {
        private int _calls;

        public string Name => "partial";

        public AgentRole Role => AgentRole.Developer;

        public Task<AgentResult> RunAsync(AgentContext context, CancellationToken cancellationToken)
        {
            _calls++;
            IReadOnlyList<GeneratedFile> files = _calls == 1
                ? [new GeneratedFile("main.tf", "bad\n"), new GeneratedFile("variables.tf", "variable \"region\" {}\n")]
                : [new GeneratedFile("main.tf", "good\n")];
            return Task.FromResult(new AgentResult([], Files: files));
        }
    }

    private sealed class SecretFileDeveloper : IAgent
    {
        public string Name => "secret-file";

        public AgentRole Role => AgentRole.Developer;

        public Task<AgentResult> RunAsync(AgentContext context, CancellationToken cancellationToken)
        {
            return Task.FromResult(new AgentResult([], Files: [new GeneratedFile("main.tf", "key AKIAIOSFODNN7EXAMPLE\n")]));
        }
    }

    private sealed class TokenReviewer : IAgent
    {
        public string Name => "security-reviewer";

        public AgentRole Role => AgentRole.Reviewer;

        public Task<AgentResult> RunAsync(AgentContext context, CancellationToken cancellationToken)
        {
            return Task.FromResult(new AgentResult([], Review: "notes", Tokens: new TokenCount(5, 7)));
        }
    }

    private sealed class FailedOrchestrator : IAgent
    {
        public string Name => "orchestrator";

        public AgentRole Role => AgentRole.Orchestrator;

        public Task<AgentResult> RunAsync(AgentContext context, CancellationToken cancellationToken)
        {
            return Task.FromResult(new AgentResult([], Tokens: new TokenCount(3, 4), Failed: true, FailureMessage: "The orchestrator did not return JSON."));
        }
    }

    private sealed class MainOnlyGate : IGate
    {
        public string Name => "main-only";

        public int Order => 1;

        public Task<GateResult> CheckAsync(Artifact artifact, CancellationToken cancellationToken)
        {
            var main = artifact.Files.First(file => file.RelativePath == "main.tf").Content;
            var passed = main.Contains("good", StringComparison.Ordinal);
            IReadOnlyList<GateIssue> issues = passed ? [] : [new GateIssue("main.tf", 1, "rule", "HIGH", "bad")];
            return Task.FromResult(new GateResult(Name, passed, issues));
        }
    }

    private sealed class LowIssueGate : IGate
    {
        public string Name => "low";

        public int Order => 1;

        public Task<GateResult> CheckAsync(Artifact artifact, CancellationToken cancellationToken)
        {
            return Task.FromResult(new GateResult(Name, true, [new GateIssue("main.tf", 1, "CKV_AWS_1", "LOW", "tag")]));
        }
    }

    private sealed class PassingGate : IGate
    {
        public string Name => "passing";

        public int Order => 1;

        public Task<GateResult> CheckAsync(Artifact artifact, CancellationToken cancellationToken)
        {
            return Task.FromResult(new GateResult(Name, true, []));
        }
    }
}

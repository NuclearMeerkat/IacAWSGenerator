using IacAgent.Core;
using IacAgent.Host;
using IacAgent.Host.Configuration;
using IacAgent.Host.Pipeline;

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
    public async Task RunAsync_TokenLimit_ReturnsBudgetExceeded()
    {
        var directory = Directory.CreateTempSubdirectory("iacagent-").FullName;
        try
        {
            var runner = CreateRunner(new SwitchingDeveloper(), new ContentFailGate(), maxTokens: 1);
            var result = await runner.RunAsync(Request(directory), CancellationToken.None);

            Assert.Equal(RunOutcome.BudgetExceeded, result.Outcome);
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

    private static PipelineRunner CreateRunner(IAgent developer, IGate gate, int? maxTokens = null)
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var catalog = new ProfileCatalog([new ScriptedProfile(developer, maxTokens)]);
        return new PipelineRunner(catalog, [gate], services, NullLogger<PipelineRunner>.Instance);
    }

    private static GenerateRequest Request(string directory, string profile = "scripted", int maxRepairs = 3)
    {
        return new GenerateRequest("hello", profile, directory, "test-model", null, null, maxRepairs, true);
    }

    private sealed class ScriptedProfile(IAgent developer, int? maxTokens) : IProfileDefinition
    {
        public string Name => "scripted";

        public PipelineProfile Build(IServiceProvider services)
        {
            return new PipelineProfile(Name, [developer], new LoopPolicy(3, maxTokens));
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

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

internal static class RepairLoopFixture
{
    internal static PipelineRunner CreateRunner(
        IAgent developer,
        IGate gate,
        int? maxTokens = null,
        int profileRepairs = 3,
        IEnumerable<IRedactionRule>? rules = null,
        IModelRouter? router = null,
        IAgent? reviewer = null,
        IAgent? orchestrator = null,
        IAgent? advisor = null)
    {
        var agents = new List<IAgent>();
        if (orchestrator is not null)
        {
            agents.Add(orchestrator);
        }

        if (advisor is not null)
        {
            agents.Add(advisor);
        }

        agents.Add(developer);
        if (reviewer is not null)
        {
            agents.Add(reviewer);
        }

        var services = new ServiceCollection().BuildServiceProvider();
        var catalog = new ProfileCatalog([new ScriptedProfile(agents, profileRepairs, maxTokens)]);
        return new PipelineRunner(catalog, [gate], services, NullLogger<PipelineRunner>.Instance, router ?? new IdleRouter(), rules ?? [], new QuietProcessRunner());
    }

    internal static GenerateRequest Request(string directory, string profile = "scripted", int? maxRepairs = 3, int? maxTokens = null)
    {
        return new GenerateRequest("hello", profile, directory, "test-model", null, null, maxRepairs, maxTokens, true);
    }
}

internal sealed class ScriptedProfile(IReadOnlyList<IAgent> agents, int profileRepairs, int? maxTokens) : IProfileDefinition
{
    public string Name => "scripted";

    public PipelineProfile Build(IServiceProvider services)
    {
        return new PipelineProfile(Name, agents, new LoopPolicy(profileRepairs, maxTokens));
    }
}

internal sealed class IdleRouter : IModelRouter
{
    public Microsoft.Extensions.AI.IChatClient GetClient(ModelTier tier)
    {
        throw new NotSupportedException();
    }

    public string GetModelId(ModelTier tier)
    {
        return "test-model";
    }

    public Microsoft.Extensions.AI.ChatOptions CreateChatOptions()
    {
        return new Microsoft.Extensions.AI.ChatOptions();
    }

    public decimal? EstimateCostUsd(string modelId, int inputTokens, int outputTokens)
    {
        return null;
    }
}

internal sealed class HostedProfile : IProfileDefinition
{
    public string Name => "hosted";

    public PipelineProfile Build(IServiceProvider services)
    {
        return new PipelineProfile(Name, [new FileDeveloper()], new LoopPolicy());
    }
}

internal sealed class FileDeveloper : IAgent
{
    public string Name => "file-developer";

    public AgentRole Role => AgentRole.Developer;

    public Task<AgentResult> RunAsync(AgentContext context, CancellationToken cancellationToken)
    {
        return Task.FromResult(new AgentResult([], Files: [new GeneratedFile("main.tf", "resource \"aws_s3_bucket\" \"logs\" {}\n")]));
    }
}

internal sealed class SwitchingDeveloper : IAgent
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

internal sealed class DroppingDeveloper : IAgent
{
    private int _calls;

    public List<IReadOnlyList<GateResult>?> Seen { get; } = [];

    public string Name => "dropping";

    public AgentRole Role => AgentRole.Developer;

    public Task<AgentResult> RunAsync(AgentContext context, CancellationToken cancellationToken)
    {
        _calls++;
        Seen.Add(context.GateResults);
        if (_calls == 1)
        {
            return Task.FromResult(new AgentResult([], Files: [new GeneratedFile("main.tf", "same\n")]));
        }

        return Task.FromResult(new AgentResult([], Failed: true, FailureMessage: "no files"));
    }
}

internal sealed class MovingLineGate : IGate
{
    private int _checks;

    public string Name => "moving";

    public int Order => 1;

    public Task<GateResult> CheckAsync(Artifact artifact, CancellationToken cancellationToken)
    {
        _checks++;
        return Task.FromResult(new GateResult(Name, false, [new GateIssue("main.tf", _checks, "same", "HIGH", $"line {_checks}")]));
    }
}

internal sealed class ContentRecordingDeveloper : IAgent
{
    private int _calls;

    public string? RepairContent { get; private set; }

    public string Name => "content-recording";

    public AgentRole Role => AgentRole.Developer;

    public Task<AgentResult> RunAsync(AgentContext context, CancellationToken cancellationToken)
    {
        _calls++;
        if (_calls > 1)
        {
            RepairContent = context.Artifact?.Files.FirstOrDefault()?.Content;
        }

        return Task.FromResult(new AgentResult([], Files: [new GeneratedFile("main.tf", "messy\n")]));
    }
}

internal sealed class RewritingGate : IGate
{
    public string Name => "rewrite";

    public int Order => 1;

    public async Task<GateResult> CheckAsync(Artifact artifact, CancellationToken cancellationToken)
    {
        var path = Path.Combine(artifact.Directory, "main.tf");
        if (File.Exists(path))
        {
            await File.WriteAllTextAsync(path, "formatted\n", cancellationToken);
        }

        return new GateResult(Name, false, [new GateIssue("main.tf", 1, "other", "HIGH", "still wrong")]);
    }
}

internal sealed class CountingDeveloper : IAgent
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

internal sealed class AlwaysFailGate : IGate
{
    public string Name => "always";

    public int Order => 1;

    public Task<GateResult> CheckAsync(Artifact artifact, CancellationToken cancellationToken)
    {
        return Task.FromResult(new GateResult(Name, false, [new GateIssue("main.tf", 1, "same", "HIGH", "same")]));
    }
}

internal sealed class BudgetGate : IGate
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

internal sealed class ContentFailGate : IGate
{
    public string Name => "content";

    public int Order => 1;

    public Task<GateResult> CheckAsync(Artifact artifact, CancellationToken cancellationToken)
    {
        var content = artifact.Files.FirstOrDefault()?.Content ?? string.Empty;
        return Task.FromResult(new GateResult(Name, false, [new GateIssue("main.tf", 1, "rule", "HIGH", content)]));
    }
}

internal sealed class PartialRepairDeveloper : IAgent
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

internal sealed class SecretFileDeveloper : IAgent
{
    public string Name => "secret-file";

    public AgentRole Role => AgentRole.Developer;

    public Task<AgentResult> RunAsync(AgentContext context, CancellationToken cancellationToken)
    {
        return Task.FromResult(new AgentResult([], Files: [new GeneratedFile("main.tf", "key AKIAIOSFODNN7EXAMPLE\n")]));
    }
}

internal sealed class TokenReviewer : IAgent
{
    public string Name => "security-reviewer";

    public AgentRole Role => AgentRole.Reviewer;

    public Task<AgentResult> RunAsync(AgentContext context, CancellationToken cancellationToken)
    {
        return Task.FromResult(new AgentResult([], Review: "notes", Tokens: new TokenCount(5, 7)));
    }
}

internal sealed class FailedOrchestrator : IAgent
{
    public string Name => "orchestrator";

    public AgentRole Role => AgentRole.Orchestrator;

    public Task<AgentResult> RunAsync(AgentContext context, CancellationToken cancellationToken)
    {
        return Task.FromResult(new AgentResult([], Tokens: new TokenCount(3, 4), Failed: true, FailureMessage: "The orchestrator did not return JSON."));
    }
}

internal sealed class MainOnlyGate : IGate
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

internal sealed class AdvisoryGate : IGate
{
    public string Name => "advisory";

    public int Order => 1;

    public Task<GateResult> CheckAsync(Artifact artifact, CancellationToken cancellationToken)
    {
        return Task.FromResult(new GateResult(Name, true, [new GateIssue("main.tf", 1, "CKV2_AWS_5", "UNKNOWN", "unattached", false)]));
    }
}

internal sealed class NoisyBlockingGate : IGate
{
    private int _checks;

    public string Name => "noisy";

    public int Order => 1;

    public Task<GateResult> CheckAsync(Artifact artifact, CancellationToken cancellationToken)
    {
        _checks++;
        return Task.FromResult(new GateResult(Name, false,
        [
            new GateIssue("main.tf", 1, "CKV_AWS_24", "UNKNOWN", "ssh", true),
                new GateIssue("main.tf", _checks, "CKV2_AWS_5", "UNKNOWN", "noise", false),
            ]));
    }
}

internal sealed class LowIssueGate : IGate
{
    public string Name => "low";

    public int Order => 1;

    public Task<GateResult> CheckAsync(Artifact artifact, CancellationToken cancellationToken)
    {
        return Task.FromResult(new GateResult(Name, true, [new GateIssue("main.tf", 1, "CKV_AWS_1", "LOW", "tag", false)]));
    }
}

internal sealed class ContextDeveloper : IAgent
{
    public List<AgentContext> Seen { get; } = [];

    public string Name => "context";

    public AgentRole Role => AgentRole.Developer;

    public Task<AgentResult> RunAsync(AgentContext context, CancellationToken cancellationToken)
    {
        Seen.Add(context);
        return Task.FromResult(new AgentResult([], Files: [new GeneratedFile("main.tf", "ok\n")]));
    }
}

internal sealed class JsonOrchestrator : IAgent
{
    public string Name => "orchestrator";

    public AgentRole Role => AgentRole.Orchestrator;

    public Task<AgentResult> RunAsync(AgentContext context, CancellationToken cancellationToken)
    {
        return Task.FromResult(new AgentResult([], RequirementsJson: """{"summary":"api"}"""));
    }
}

internal sealed class NoteAdvisor(string note) : IAgent
{
    public string Name => "architect";

    public AgentRole Role => AgentRole.Advisor;

    public Task<AgentResult> RunAsync(AgentContext context, CancellationToken cancellationToken)
    {
        return Task.FromResult(new AgentResult([], Review: note));
    }
}

internal sealed class TwoFileDeveloper : IAgent
{
    private int _calls;

    public List<AgentContext> Seen { get; } = [];

    public string Name => "two-file";

    public AgentRole Role => AgentRole.Developer;

    public Task<AgentResult> RunAsync(AgentContext context, CancellationToken cancellationToken)
    {
        _calls++;
        Seen.Add(context);
        IReadOnlyList<GeneratedFile> files = _calls == 1
            ? [new GeneratedFile("main.tf", "resource \"aws_s3_bucket\" \"bad\" {}\n"), new GeneratedFile("variables.tf", "resource \"aws_s3_bucket\" \"data\" {}\nkeep-me-out\n")]
            : [new GeneratedFile("main.tf", "resource \"aws_s3_bucket\" \"bad\" {}\n")];
        return Task.FromResult(new AgentResult([], Files: files));
    }
}

internal sealed class MainFileGate : IGate
{
    public string Name => "main-only";

    public int Order => 1;

    public Task<GateResult> CheckAsync(Artifact artifact, CancellationToken cancellationToken)
    {
        return Task.FromResult(new GateResult(Name, false, [new GateIssue("main.tf", 1, "rule", "HIGH", "bad")]));
    }
}

internal sealed class PassingGate : IGate
{
    public string Name => "passing";

    public int Order => 1;

    public Task<GateResult> CheckAsync(Artifact artifact, CancellationToken cancellationToken)
    {
        return Task.FromResult(new GateResult(Name, true, []));
    }
}

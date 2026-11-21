using IacAgent.Core;
using IacAgent.Host.Configuration;
using IacAgent.Host.Doctor;
using IacAgent.Host.Recording;

using Microsoft.Extensions.Logging;

namespace IacAgent.Host.Pipeline;

/// <summary>Runs a profile, the host gates, and the repair loop, then writes run.json.</summary>
public sealed class PipelineRunner
{
    private readonly ProfileCatalog _catalog;
    private readonly IReadOnlyList<IGate> _gates;
    private readonly IServiceProvider _services;
    private readonly ILogger<PipelineRunner> _logger;
    private readonly IModelRouter _router;
    private readonly IReadOnlyList<IRedactionRule> _rules;
    private readonly IProcessRunner _processRunner;

    /// <summary>Creates a runner from the registered profiles and gates.</summary>
    public PipelineRunner(
        ProfileCatalog catalog,
        IEnumerable<IGate> gates,
        IServiceProvider services,
        ILogger<PipelineRunner> logger,
        IModelRouter router,
        IEnumerable<IRedactionRule> rules,
        IProcessRunner processRunner)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(gates);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(processRunner);
        _catalog = catalog;
        _gates = gates.OrderBy(gate => gate.Order).ToList();
        _services = services;
        _logger = logger;
        _router = router;
        _rules = rules.ToList();
        _processRunner = processRunner;
    }

    /// <summary>Runs one generate request.</summary>
    public async Task<PipelineResult> RunAsync(GenerateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var outputDirectory = Path.GetFullPath(request.OutputDirectory);
        var recorder = new RunRecorder(request, _rules, _router.GetModelId(ModelTier.Strong), _router.GetModelId(ModelTier.Cheap), request.Security);
        var workspace = RunWorkspace.Create(recorder.RunId);
        Exception? thrown = null;
        try
        {
            try
            {
                return await ExecuteAsync(request, workspace, outputDirectory, recorder, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                thrown = exception;
                await WriteFailureAsync(workspace, recorder, exception, cancellationToken).ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            PublishWorkspace(workspace, outputDirectory, request.Verbose, thrown);
        }
    }

    private async Task<PipelineResult> ExecuteAsync(
        GenerateRequest request,
        string workspace,
        string outputDirectory,
        RunRecorder recorder,
        CancellationToken cancellationToken)
    {
        var profile = _catalog.Create(request.Profile, _services);
        await RecordToolVersionsAsync(recorder, cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            _logger.LogWarning("Profile {Profile} is not registered.", request.Profile);
            await recorder.WriteAsync(workspace, RunOutcome.ConfigError, cancellationToken).ConfigureAwait(false);
            return new PipelineResult(RunOutcome.ConfigError, outputDirectory);
        }

        var loop = EffectiveLoop(request, profile);
        var provider = string.IsNullOrWhiteSpace(request.Endpoint) ? "openrouter" : "local";
        _logger.LogInformation(
            "Running profile {Profile}. Provider {Provider}. Repair limit {MaxRepairIterations}.",
            profile.Name,
            provider,
            loop.MaxRepairIterations);

        var assumptions = new List<string>();
        var opening = await RunOrchestratorsAsync(profile, request, assumptions, cancellationToken).ConfigureAwait(false);
        if (opening.Kind == OpeningKind.NeedsHuman)
        {
            recorder.RecordIteration(new IterationRecord(1, [], opening.Tokens));
            recorder.AddAssumptions(assumptions);
            await recorder.WriteAsync(workspace, RunOutcome.NeedsHuman, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Run finished with {Outcome}.", RunOutcome.NeedsHuman);
            return new PipelineResult(RunOutcome.NeedsHuman, outputDirectory);
        }

        if (opening.Kind == OpeningKind.Failed)
        {
            return await StopOpeningAsync(workspace, outputDirectory, assumptions, recorder, opening, cancellationToken).ConfigureAwait(false);
        }

        var advised = await RunAdvisorsAsync(profile, request, opening.Requirements, assumptions, opening.Tokens, cancellationToken).ConfigureAwait(false);
        var repair = await new RepairLoop(_gates, _logger, _rules).RunAsync(
            profile,
            request,
            workspace,
            advised.Requirements,
            advised.DesignNotes,
            assumptions,
            recorder,
            advised.Tokens,
            loop,
            cancellationToken).ConfigureAwait(false);
        recorder.AddAssumptions(assumptions);
        recorder.SetFiles(repair.Files.Select(file => file.RelativePath));
        recorder.SetResources(TerraformResourceInventory.Count(repair.Files));
        await recorder.WriteAsync(workspace, repair.Outcome, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Run finished with {Outcome}.", repair.Outcome);
        return new PipelineResult(repair.Outcome, outputDirectory);
    }

    private async Task RecordToolVersionsAsync(RunRecorder recorder, CancellationToken cancellationToken)
    {
        var terraform = await ToolVersionReader.ReadAsync(_processRunner, "terraform", ["version"], cancellationToken).ConfigureAwait(false);
        var checkov = await ToolVersionReader.ReadAsync(_processRunner, "checkov", ["--version"], cancellationToken).ConfigureAwait(false);
        recorder.SetTools(terraform.Ok ? terraform.Detail : string.Empty, checkov.Ok ? checkov.Detail : string.Empty);
    }

    private async Task WriteFailureAsync(
        string outputDirectory,
        RunRecorder recorder,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var cancelled = exception is OperationCanceledException && cancellationToken.IsCancellationRequested;
        if (!cancelled)
        {
            _logger.LogError(exception, "Run failed.");
        }

        recorder.SetError(FirstLine(exception));
        try
        {
            await recorder.WriteAsync(outputDirectory, cancelled ? RunOutcome.Cancelled : RunOutcome.Error, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception writeException)
        {
            _logger.LogError(writeException, "Could not write run.json after the run failed.");
        }
    }

    private static string FirstLine(Exception exception)
    {
        var message = exception.Message;
        var end = message.IndexOfAny(['\r', '\n']);
        var line = end < 0 ? message : message[..end];
        return string.IsNullOrWhiteSpace(line) ? exception.GetType().Name : line.Trim();
    }

    private async Task<Opening> RunOrchestratorsAsync(
        PipelineProfile profile,
        GenerateRequest request,
        List<string> assumptions,
        CancellationToken cancellationToken)
    {
        string? requirements = null;
        var tokens = new TokenCount(0, 0);
        foreach (var agent in profile.Agents.Where(agent => agent.Role == AgentRole.Orchestrator))
        {
            _logger.LogInformation("Running agent {Agent}.", agent.Name);
            var result = await agent.RunAsync(new AgentContext(request.Prompt, request.AutoApprove), cancellationToken).ConfigureAwait(false);
            assumptions.AddRange(result.Assumptions);
            tokens = Add(tokens, result.Tokens);
            if (result.Failed)
            {
                assumptions.Add(result.FailureMessage ?? "The orchestrator failed.");
                return new Opening(requirements, tokens, OpeningKind.Failed, result.FailureMessage);
            }

            if (result.NeedsHuman)
            {
                return new Opening(requirements, tokens, OpeningKind.NeedsHuman, result.FailureMessage);
            }

            if (!string.IsNullOrWhiteSpace(result.RequirementsJson))
            {
                requirements = result.RequirementsJson;
            }
        }

        return new Opening(requirements, tokens, OpeningKind.Continue, null);
    }

    private async Task<(string? Requirements, string? DesignNotes, TokenCount Tokens)> RunAdvisorsAsync(
        PipelineProfile profile,
        GenerateRequest request,
        string? requirements,
        List<string> assumptions,
        TokenCount tokens,
        CancellationToken cancellationToken)
    {
        var notes = new List<string>();
        foreach (var agent in profile.Agents.Where(agent => agent.Role == AgentRole.Advisor))
        {
            _logger.LogInformation("Running agent {Agent}.", agent.Name);
            var result = await agent.RunAsync(
                new AgentContext(request.Prompt, request.AutoApprove, requirements),
                cancellationToken).ConfigureAwait(false);
            assumptions.AddRange(result.Assumptions);
            tokens = Add(tokens, result.Tokens);
            if (!string.IsNullOrWhiteSpace(result.Review))
            {
                notes.Add(result.Review.Trim());
            }
        }

        var designNotes = notes.Count == 0 ? null : string.Join("\n\n", notes);
        return (requirements, designNotes, tokens);
    }

    private static LoopPolicy EffectiveLoop(GenerateRequest request, PipelineProfile profile)
    {
        return new LoopPolicy(
            request.MaxRepairIterations ?? profile.Loop.MaxRepairIterations,
            request.MaxTotalTokens ?? profile.Loop.MaxTotalTokens);
    }

    private async Task<PipelineResult> StopOpeningAsync(
        string workspace,
        string outputDirectory,
        List<string> assumptions,
        RunRecorder recorder,
        Opening opening,
        CancellationToken cancellationToken)
    {
        var message = opening.FailureMessage ?? "The orchestrator failed.";
        var results = new GateResult[]
        {
            new("orchestrator", false, [new GateIssue(string.Empty, null, "orchestrator", "error", message)]),
        };
        recorder.RecordIteration(new IterationRecord(1, [new GateSummary("orchestrator", false, 1, 1)], opening.Tokens));
        recorder.AddAssumptions(assumptions);
        await EscalationReport.WriteAsync(workspace, RunOutcome.GateFailed, results, [], _rules, cancellationToken).ConfigureAwait(false);
        await recorder.WriteAsync(workspace, RunOutcome.GateFailed, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Run finished with {Outcome}.", RunOutcome.GateFailed);
        return new PipelineResult(RunOutcome.GateFailed, outputDirectory);
    }

    private enum OpeningKind
    {
        Continue,
        NeedsHuman,
        Failed,
    }

    private sealed record Opening(string? Requirements, TokenCount Tokens, OpeningKind Kind, string? FailureMessage);

    private void PublishWorkspace(string workspace, string outputDirectory, bool verbose, Exception? thrown)
    {
        try
        {
            RunWorkspace.Publish(workspace, outputDirectory);
            if (verbose)
            {
                _logger.LogInformation("Workspace kept at {Workspace}.", workspace);
            }
            else
            {
                RunWorkspace.Delete(workspace);
            }
        }
        catch (Exception exception)
        {
            if (thrown is null)
            {
                throw;
            }

            _logger.LogError(exception, "Could not copy the workspace to {Output}.", outputDirectory);
        }
    }

    private static TokenCount Add(TokenCount current, TokenCount? addition)
    {
        return addition is null ? current : current.Plus(addition);
    }
}

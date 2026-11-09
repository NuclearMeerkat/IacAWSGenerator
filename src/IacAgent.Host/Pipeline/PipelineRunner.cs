using IacAgent.Core;
using IacAgent.Host.Configuration;
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

    /// <summary>Creates a runner from the registered profiles and gates.</summary>
    public PipelineRunner(
        ProfileCatalog catalog,
        IEnumerable<IGate> gates,
        IServiceProvider services,
        ILogger<PipelineRunner> logger)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(gates);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(logger);
        _catalog = catalog;
        _gates = gates.OrderBy(gate => gate.Order).ToList();
        _services = services;
        _logger = logger;
    }

    /// <summary>Runs one generate request.</summary>
    public async Task<PipelineResult> RunAsync(GenerateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var outputDirectory = Path.GetFullPath(request.OutputDirectory);
        var recorder = new RunRecorder(request);
        var profile = _catalog.Create(request.Profile, _services);
        if (profile is null)
        {
            _logger.LogWarning("Profile {Profile} is not registered.", request.Profile);
            await recorder.WriteAsync(outputDirectory, RunOutcome.ConfigError, cancellationToken).ConfigureAwait(false);
            return new PipelineResult(RunOutcome.ConfigError, outputDirectory);
        }

        var provider = string.IsNullOrWhiteSpace(request.Endpoint) ? "openrouter" : "local";
        _logger.LogInformation(
            "Running profile {Profile}. Provider {Provider}. Repair limit {MaxRepairIterations}.",
            profile.Name,
            provider,
            request.MaxRepairIterations);

        var assumptions = new List<string>();
        var (requirements, tokens, needsHuman) = await RunOrchestratorsAsync(profile, request, assumptions, cancellationToken).ConfigureAwait(false);
        if (needsHuman)
        {
            recorder.AddAssumptions(assumptions);
            await recorder.WriteAsync(outputDirectory, RunOutcome.NeedsHuman, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Run finished with {Outcome}.", RunOutcome.NeedsHuman);
            return new PipelineResult(RunOutcome.NeedsHuman, outputDirectory);
        }

        (requirements, tokens) = await RunAdvisorsAsync(profile, request, requirements, assumptions, tokens, cancellationToken).ConfigureAwait(false);
        var outcome = await RunDevelopersAsync(profile, request, outputDirectory, requirements, assumptions, recorder, tokens, cancellationToken).ConfigureAwait(false);
        recorder.AddAssumptions(assumptions);
        await recorder.WriteAsync(outputDirectory, outcome, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Run finished with {Outcome}.", outcome);
        return new PipelineResult(outcome, outputDirectory);
    }

    private async Task<(string? Requirements, TokenCount Tokens, bool NeedsHuman)> RunOrchestratorsAsync(
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
            if (result.NeedsHuman)
            {
                return (requirements, tokens, true);
            }

            if (!string.IsNullOrWhiteSpace(result.RequirementsJson))
            {
                requirements = result.RequirementsJson;
            }
        }

        return (requirements, tokens, false);
    }

    private async Task<(string? Requirements, TokenCount Tokens)> RunAdvisorsAsync(
        PipelineProfile profile,
        GenerateRequest request,
        string? requirements,
        List<string> assumptions,
        TokenCount tokens,
        CancellationToken cancellationToken)
    {
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
                requirements = string.IsNullOrWhiteSpace(requirements)
                    ? result.Review
                    : requirements + "\n\n" + result.Review;
            }
        }

        return (requirements, tokens);
    }

    private async Task<RunOutcome> RunDevelopersAsync(
        PipelineProfile profile,
        GenerateRequest request,
        string outputDirectory,
        string? requirements,
        List<string> assumptions,
        RunRecorder recorder,
        TokenCount tokens,
        CancellationToken cancellationToken)
    {
        var developers = profile.Agents.Where(agent => agent.Role == AgentRole.Developer).ToList();
        var files = new List<GeneratedFile>();
        IReadOnlyList<GateResult> lastResults = [];
        string? previousSignature = null;
        var repairs = 0;
        var orchestratorTokens = tokens;

        while (true)
        {
            var iterationTokens = new TokenCount(0, 0);
            var failingFiles = repairs == 0 ? null : FailingFiles(files, lastResults);
            var context = new AgentContext(
                request.Prompt,
                request.AutoApprove,
                requirements,
                failingFiles is null ? null : new Artifact(outputDirectory, failingFiles),
                repairs == 0 ? null : lastResults);

            var failedWithoutFiles = false;
            string? failureMessage = null;
            foreach (var developer in developers)
            {
                _logger.LogInformation("Running agent {Agent}.", developer.Name);
                var result = await developer.RunAsync(context, cancellationToken).ConfigureAwait(false);
                assumptions.AddRange(result.Assumptions);
                iterationTokens = Add(iterationTokens, result.Tokens);
                tokens = Add(tokens, result.Tokens);
                if (result.Files is not null)
                {
                    files = result.Files.ToList();
                }

                failedWithoutFiles = result.Failed && result.Files is null;
                failureMessage = result.FailureMessage;
            }

            if (failedWithoutFiles)
            {
                lastResults = [new GateResult("developer", false, [new GateIssue(string.Empty, null, "developer-output", "error", failureMessage ?? "The developer returned no files.")])];
            }
            else if (files.Count > 0)
            {
                WorkspaceFiles.ReplaceTerraformFiles(outputDirectory, files);
                lastResults = await RunGatesAsync(new Artifact(outputDirectory, files), cancellationToken).ConfigureAwait(false);
            }
            else
            {
                lastResults = await RunGatesAsync(new Artifact(outputDirectory, files), cancellationToken).ConfigureAwait(false);
            }

            if (repairs == 0)
            {
                iterationTokens = Add(orchestratorTokens, iterationTokens);
            }

            recorder.RecordIteration(new IterationRecord(
                repairs + 1,
                lastResults.Select(result => new GateSummary(result.GateName, result.Passed, result.Issues.Count)).ToList(),
                iterationTokens));

            if (lastResults.All(result => result.Passed))
            {
                await RunReviewerAsync(profile, request, outputDirectory, requirements, files, lastResults, assumptions, cancellationToken).ConfigureAwait(false);
                return RunOutcome.Succeeded;
            }

            if (OverTokenLimit(profile, tokens))
            {
                await EscalationReport.WriteAsync(outputDirectory, RunOutcome.BudgetExceeded, lastResults, cancellationToken).ConfigureAwait(false);
                return RunOutcome.BudgetExceeded;
            }

            var signature = IssueSignature.Create(lastResults);
            if (previousSignature is not null && signature == previousSignature)
            {
                return await StopAsync(outputDirectory, lastResults, cancellationToken).ConfigureAwait(false);
            }

            if (repairs >= request.MaxRepairIterations)
            {
                return await StopAsync(outputDirectory, lastResults, cancellationToken).ConfigureAwait(false);
            }

            previousSignature = signature;
            repairs++;
        }
    }

    private static async Task<RunOutcome> StopAsync(
        string outputDirectory,
        IReadOnlyList<GateResult> results,
        CancellationToken cancellationToken)
    {
        var outcome = FailureOutcome(results);
        await EscalationReport.WriteAsync(outputDirectory, outcome, results, cancellationToken).ConfigureAwait(false);
        return outcome;
    }

    private static RunOutcome FailureOutcome(IReadOnlyList<GateResult> results)
    {
        var failedIssues = results.Where(result => !result.Passed).SelectMany(result => result.Issues).ToList();
        if (failedIssues.Count > 0 && failedIssues.All(issue => issue.RuleId == KnownGateRules.BudgetExceeded))
        {
            return RunOutcome.BudgetExceeded;
        }

        return RunOutcome.GateFailed;
    }

    private async Task<IReadOnlyList<GateResult>> RunGatesAsync(Artifact artifact, CancellationToken cancellationToken)
    {
        var results = new List<GateResult>();
        foreach (var gate in _gates)
        {
            _logger.LogInformation("Running gate {Gate}.", gate.Name);
            results.Add(await gate.CheckAsync(artifact, cancellationToken).ConfigureAwait(false));
        }

        return results;
    }

    private async Task RunReviewerAsync(
        PipelineProfile profile,
        GenerateRequest request,
        string outputDirectory,
        string? requirements,
        IReadOnlyList<GeneratedFile> files,
        IReadOnlyList<GateResult> results,
        List<string> assumptions,
        CancellationToken cancellationToken)
    {
        var reviewer = profile.Agents.FirstOrDefault(agent => agent.Role == AgentRole.Reviewer);
        if (reviewer is null || !results.SelectMany(result => result.Issues).Any(IsLowSeverity))
        {
            return;
        }

        var result = await reviewer.RunAsync(
            new AgentContext(request.Prompt, request.AutoApprove, requirements, new Artifact(outputDirectory, files), results),
            cancellationToken).ConfigureAwait(false);
        assumptions.AddRange(result.Assumptions);
        if (!string.IsNullOrWhiteSpace(result.Review))
        {
            await File.WriteAllTextAsync(Path.Combine(outputDirectory, "review.md"), result.Review, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool OverTokenLimit(PipelineProfile profile, TokenCount tokens)
    {
        var limit = profile.Loop.MaxTotalTokens;
        return limit is not null && tokens.Input + tokens.Output > limit.Value;
    }

    private static TokenCount Add(TokenCount current, TokenCount? addition)
    {
        if (addition is null)
        {
            return current;
        }

        return new TokenCount(current.Input + addition.Input, current.Output + addition.Output);
    }

    private static IReadOnlyList<GeneratedFile> FailingFiles(IReadOnlyList<GeneratedFile> files, IReadOnlyList<GateResult> results)
    {
        var names = results
            .SelectMany(result => result.Issues)
            .Select(issue => issue.File)
            .Where(file => !string.IsNullOrWhiteSpace(file))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (names.Count == 0)
        {
            return files;
        }

        var matched = files.Where(file => names.Contains(file.RelativePath) || names.Contains(Path.GetFileName(file.RelativePath))).ToList();
        return matched.Count == 0 ? files : matched;
    }

    private static bool IsLowSeverity(GateIssue issue)
    {
        return issue.Severity.Equals("LOW", StringComparison.OrdinalIgnoreCase)
            || issue.Severity.Equals("INFO", StringComparison.OrdinalIgnoreCase)
            || issue.Severity.Equals("UNKNOWN", StringComparison.OrdinalIgnoreCase);
    }
}

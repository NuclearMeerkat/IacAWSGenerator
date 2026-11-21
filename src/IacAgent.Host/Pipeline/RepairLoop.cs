using IacAgent.Core;
using IacAgent.Host.Configuration;
using IacAgent.Host.Recording;
using IacAgent.Host.Redaction;

using Microsoft.Extensions.Logging;

namespace IacAgent.Host.Pipeline;

/// <summary>Asks the developer for files, runs the gates, and stops on the loop rules.</summary>
internal sealed class RepairLoop(IReadOnlyList<IGate> gates, ILogger logger, IReadOnlyList<IRedactionRule> rules)
{
    private readonly Redactor _redactor = new(rules);
    public async Task<RepairResult> RunAsync(
        PipelineProfile profile,
        GenerateRequest request,
        string outputDirectory,
        string? requirements,
        string? designNotes,
        List<string> assumptions,
        RunRecorder recorder,
        TokenCount openingTokens,
        LoopPolicy loop,
        CancellationToken cancellationToken)
    {
        var developers = profile.Agents.Where(agent => agent.Role == AgentRole.Developer).ToList();
        var state = new RepairState([], [], null, 0, openingTokens, [], []);
        while (true)
        {
            var turn = await AskDeveloperAsync(developers, request, outputDirectory, requirements, designNotes, assumptions, state, cancellationToken).ConfigureAwait(false);
            state = await WriteAndCheckAsync(outputDirectory, state, turn, cancellationToken).ConfigureAwait(false);
            RecordIteration(recorder, state, turn.IterationTokens, openingTokens);
            var next = DecideNext(state, loop);
            if (next == NextStep.Succeed)
            {
                recorder.Include(await RunReviewerAsync(profile, request, outputDirectory, requirements, state, assumptions, cancellationToken).ConfigureAwait(false));
                return new RepairResult(RunOutcome.Succeeded, state.Files);
            }

            if (next == NextStep.Stop)
            {
                var outcome = await StopAsync(outputDirectory, state, cancellationToken).ConfigureAwait(false);
                return new RepairResult(outcome, state.Files);
            }

            state = state with { PreviousSignature = IssueSignature.Create(state.LastResults), Repairs = state.Repairs + 1 };
        }
    }

    private async Task<DeveloperTurn> AskDeveloperAsync(
        IReadOnlyList<IAgent> developers,
        GenerateRequest request,
        string outputDirectory,
        string? requirements,
        string? designNotes,
        List<string> assumptions,
        RepairState state,
        CancellationToken cancellationToken)
    {
        var context = RepairContext.ForDeveloper(
            request,
            outputDirectory,
            requirements,
            designNotes,
            state.Files,
            state.LastResults,
            state.Repairs);
        var iteration = new TokenCount(0, 0);
        var tokens = state.Tokens;
        var files = state.Files;
        var failedWithoutFiles = false;
        string? failureMessage = null;
        foreach (var developer in developers)
        {
            logger.LogInformation("Running agent {Agent}.", developer.Name);
            var result = await developer.RunAsync(context, cancellationToken).ConfigureAwait(false);
            assumptions.AddRange(result.Assumptions);
            iteration = Add(iteration, result.Tokens);
            tokens = Add(tokens, result.Tokens);
            if (result.Files is not null)
            {
                files = state.Repairs == 0 ? result.Files.ToList() : WorkspaceFiles.Merge(files, result.Files);
            }

            failedWithoutFiles = result.Failed && result.Files is null;
            failureMessage = result.FailureMessage;
        }

        return new DeveloperTurn(files, iteration, tokens, failedWithoutFiles, failureMessage);
    }

    private async Task<RepairState> WriteAndCheckAsync(
        string outputDirectory,
        RepairState state,
        DeveloperTurn turn,
        CancellationToken cancellationToken)
    {
        if (turn.FailedWithoutFiles && state.Repairs > 0)
        {
            var failure = DeveloperFailure(turn.FailureMessage);
            var kept = state.LastResults.Where(result => result.GateName != "developer").Append(failure).ToList();
            return state with { Files = turn.Files, LastResults = kept, IterationResults = [failure], Tokens = turn.TotalTokens, GateDurations = [] };
        }

        if (turn.FailedWithoutFiles)
        {
            var failure = DeveloperFailure(turn.FailureMessage);
            return state with { Files = turn.Files, LastResults = [failure], IterationResults = [failure], Tokens = turn.TotalTokens, GateDurations = [] };
        }

        if (turn.Files.Count > 0)
        {
            WorkspaceFiles.ReplaceTerraformFiles(outputDirectory, turn.Files);
        }

        var measured = await RunGatesAsync(new Artifact(outputDirectory, turn.Files), cancellationToken).ConfigureAwait(false);
        var files = Reload(outputDirectory, turn.Files);
        var results = measured.Select(pass => pass.Result).ToList();
        return state with
        {
            Files = files,
            LastResults = results,
            IterationResults = results,
            Tokens = turn.TotalTokens,
            GateDurations = measured.Select(pass => pass.DurationMs).ToList(),
        };
    }

    private static IReadOnlyList<GeneratedFile> Reload(string directory, IReadOnlyList<GeneratedFile> files)
    {
        return files.Select(file => ReloadFile(directory, file)).ToList();
    }

    private static GeneratedFile ReloadFile(string directory, GeneratedFile file)
    {
        var relative = file.RelativePath.Replace('\\', '/').TrimStart('/');
        var path = Path.GetFullPath(Path.Combine(directory, relative));
        var root = Path.GetFullPath(directory + Path.DirectorySeparatorChar);
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
        {
            return file;
        }

        return file with { Content = File.ReadAllText(path) };
    }

    private static void RecordIteration(RunRecorder recorder, RepairState state, TokenCount iterationTokens, TokenCount openingTokens)
    {
        var tokens = state.Repairs == 0 ? Add(openingTokens, iterationTokens) : iterationTokens;
        var gates = state.IterationResults.Select((result, index) => new GateSummary(
            result.GateName,
            result.Passed,
            result.Issues.Count,
            result.Issues.Count(issue => issue.IsBlocking),
            index < state.GateDurations.Count ? state.GateDurations[index] : 0)).ToList();
        recorder.RecordIteration(new IterationRecord(state.Repairs + 1, gates, tokens));
    }

    private static NextStep DecideNext(RepairState state, LoopPolicy loop)
    {
        if (state.LastResults.All(result => result.Passed))
        {
            return NextStep.Succeed;
        }

        if (OverTokenLimit(loop, state.Tokens))
        {
            return NextStep.Stop;
        }

        var signature = IssueSignature.Create(state.LastResults);
        if (state.PreviousSignature is not null && signature == state.PreviousSignature)
        {
            return NextStep.Stop;
        }

        return state.Repairs >= loop.MaxRepairIterations ? NextStep.Stop : NextStep.Continue;
    }

    private async Task<RunOutcome> StopAsync(string outputDirectory, RepairState state, CancellationToken cancellationToken)
    {
        var outcome = FailureOutcome(state.LastResults);
        await EscalationReport.WriteAsync(outputDirectory, outcome, state.LastResults, state.Files, rules, cancellationToken).ConfigureAwait(false);
        return outcome;
    }

    private async Task<TokenCount> RunReviewerAsync(
        PipelineProfile profile,
        GenerateRequest request,
        string outputDirectory,
        string? requirements,
        RepairState state,
        List<string> assumptions,
        CancellationToken cancellationToken)
    {
        var reviewer = profile.Agents.FirstOrDefault(agent => agent.Role == AgentRole.Reviewer);
        if (reviewer is null || !state.LastResults.SelectMany(result => result.Issues).Any(issue => !issue.IsBlocking))
        {
            return new TokenCount(0, 0);
        }

        var result = await reviewer.RunAsync(
            new AgentContext(request.Prompt, request.AutoApprove, requirements, new Artifact(outputDirectory, state.Files), state.LastResults),
            cancellationToken).ConfigureAwait(false);
        assumptions.AddRange(result.Assumptions);
        if (!string.IsNullOrWhiteSpace(result.Review))
        {
            await File.WriteAllTextAsync(Path.Combine(outputDirectory, "review.md"), _redactor.Redact(result.Review), cancellationToken).ConfigureAwait(false);
        }

        return result.Tokens ?? new TokenCount(0, 0);
    }

    private async Task<IReadOnlyList<GatePass>> RunGatesAsync(Artifact artifact, CancellationToken cancellationToken)
    {
        var results = new List<GatePass>();
        foreach (var gate in gates)
        {
            logger.LogInformation("Running gate {Gate}.", gate.Name);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var result = await gate.CheckAsync(artifact, cancellationToken).ConfigureAwait(false);
            watch.Stop();
            results.Add(new GatePass(result, watch.ElapsedMilliseconds));
        }

        return results;
    }

    private static GateResult DeveloperFailure(string? failureMessage)
    {
        var message = failureMessage ?? "The developer returned no files.";
        return new GateResult("developer", false, [new GateIssue(string.Empty, null, "developer-output", "error", message)]);
    }

    private static RunOutcome FailureOutcome(IReadOnlyList<GateResult> results)
    {
        var failed = results.Where(result => !result.Passed).SelectMany(result => result.Issues).ToList();
        if (failed.Count > 0 && failed.All(issue => issue.RuleId == KnownGateRules.BudgetExceeded))
        {
            return RunOutcome.BudgetExceeded;
        }

        return RunOutcome.GateFailed;
    }

    private static bool OverTokenLimit(LoopPolicy loop, TokenCount tokens)
    {
        return loop.MaxTotalTokens is not null && tokens.Input + tokens.Output > loop.MaxTotalTokens.Value;
    }

    private static TokenCount Add(TokenCount current, TokenCount? addition)
    {
        return addition is null ? current : current.Plus(addition);
    }

    private sealed record RepairState(
        IReadOnlyList<GeneratedFile> Files,
        IReadOnlyList<GateResult> LastResults,
        string? PreviousSignature,
        int Repairs,
        TokenCount Tokens,
        IReadOnlyList<long> GateDurations,
        IReadOnlyList<GateResult> IterationResults);

    private readonly record struct GatePass(GateResult Result, long DurationMs);

    private sealed record DeveloperTurn(
        IReadOnlyList<GeneratedFile> Files,
        TokenCount IterationTokens,
        TokenCount TotalTokens,
        bool FailedWithoutFiles,
        string? FailureMessage);

    internal sealed record RepairResult(RunOutcome Outcome, IReadOnlyList<GeneratedFile> Files);

    private enum NextStep
    {
        Continue,
        Succeed,
        Stop,
    }
}

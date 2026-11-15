using System.Text.Json;

using IacAgent.Core;

namespace IacAgent.Plugins.Gates.Checkov;

/// <summary>Runs Checkov and blocks only the findings the host policy marks as blocking.</summary>
internal sealed class CheckovGate(IProcessRunner processRunner, CheckovOptions options) : IGate
{
    /// <inheritdoc />
    public string Name => "checkov";

    /// <inheritdoc />
    public int Order => 20;

    /// <inheritdoc />
    public async Task<GateResult> CheckAsync(Artifact artifact, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        ProcessResult result;
        try
        {
            result = await processRunner.RunAsync(
                "checkov",
                ["-d", ".", "--framework", "terraform", "-o", "json", "--quiet"],
                artifact.Directory,
                cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
        {
            return new GateResult(Name, false, [new GateIssue(string.Empty, null, "checkov", "error", exception.Message)]);
        }

        try
        {
            var policy = new CheckovPolicy(options);
            var issues = CheckovOutputParser.Parse(result.StandardOutput)
                .Select(issue => issue with { IsBlocking = policy.IsBlocking(issue.RuleId) })
                .Where(issue => issue.IsBlocking || !issue.IsSuppressed)
                .ToList();
            var passed = issues.All(issue => !issue.IsBlocking);
            return new GateResult(Name, passed, issues);
        }
        catch (JsonException)
        {
            return new GateResult(Name, false, [new GateIssue(string.Empty, null, "checkov", "error", "Checkov did not return JSON.")]);
        }
    }

}

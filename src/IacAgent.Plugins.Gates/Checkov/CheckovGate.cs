using System.Text.Json;

using IacAgent.Core;

namespace IacAgent.Plugins.Gates.Checkov;

/// <summary>Runs Checkov and fails on medium and above, plus open admin ports even when Checkov omits a severity.</summary>
internal sealed class CheckovGate(IProcessRunner processRunner) : IGate
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
            var issues = CheckovOutputParser.Parse(result.StandardOutput);
            var passed = issues.All(issue => !CheckovOutputParser.FailsGate(issue));
            return new GateResult(Name, passed, issues);
        }
        catch (JsonException)
        {
            return new GateResult(Name, false, [new GateIssue(string.Empty, null, "checkov", "error", "Checkov did not return JSON.")]);
        }
    }
}

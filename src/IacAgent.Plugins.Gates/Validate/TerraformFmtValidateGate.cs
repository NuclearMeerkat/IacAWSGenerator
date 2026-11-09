using System.Text.Json;

using IacAgent.Core;

namespace IacAgent.Plugins.Gates.Validate;

/// <summary>Runs terraform fmt, init, and validate.</summary>
internal sealed class TerraformFmtValidateGate(IProcessRunner processRunner) : IGate
{
    /// <inheritdoc />
    public string Name => "validate";

    /// <inheritdoc />
    public int Order => 10;

    /// <inheritdoc />
    public async Task<GateResult> CheckAsync(Artifact artifact, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        var issues = new List<GateIssue>();
        issues.AddRange(await RunFmtAsync(artifact.Directory, cancellationToken).ConfigureAwait(false));
        await RunInitAsync(artifact.Directory, issues, cancellationToken).ConfigureAwait(false);
        issues.AddRange(await RunValidateAsync(artifact.Directory, cancellationToken).ConfigureAwait(false));
        return new GateResult(Name, issues.Count == 0, issues);
    }

    private async Task<IReadOnlyList<GateIssue>> RunFmtAsync(string directory, CancellationToken cancellationToken)
    {
        var result = await RunAsync("terraform", ["fmt", "-check", "-recursive"], directory, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode < 0)
        {
            return [new GateIssue(string.Empty, null, "terraform-fmt", "error", result.StandardError)];
        }

        if (result.ExitCode == 0)
        {
            return [];
        }

        var files = result.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (files.Length == 0)
        {
            return [new GateIssue(string.Empty, null, "terraform-fmt", "error", FirstLine(result.StandardError, "terraform fmt failed."))];
        }

        return files.Select(file => new GateIssue(file, null, "terraform-fmt", "error", "File is not formatted.")).ToList();
    }

    private async Task RunInitAsync(string directory, List<GateIssue> issues, CancellationToken cancellationToken)
    {
        var result = await RunAsync("terraform", ["init", "-backend=false", "-input=false"], directory, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode < 0)
        {
            issues.Add(new GateIssue(string.Empty, null, "terraform-init", "error", result.StandardError));
            return;
        }

        if (result.ExitCode != 0)
        {
            issues.Add(new GateIssue(string.Empty, null, "terraform-init", "error", FirstLine(result.StandardError, "terraform init failed.")));
        }
    }

    private async Task<IReadOnlyList<GateIssue>> RunValidateAsync(string directory, CancellationToken cancellationToken)
    {
        var result = await RunAsync("terraform", ["validate", "-json"], directory, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode < 0)
        {
            return [new GateIssue(string.Empty, null, "terraform-validate", "error", result.StandardError)];
        }

        try
        {
            return TerraformValidateOutputParser.Parse(result.StandardOutput);
        }
        catch (JsonException)
        {
            return [new GateIssue(string.Empty, null, "terraform-validate", "error", FirstLine(result.StandardError, "terraform validate did not return JSON."))];
        }
    }

    private async Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string directory,
        CancellationToken cancellationToken)
    {
        try
        {
            return await processRunner.RunAsync(fileName, arguments, directory, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
        {
            return new ProcessResult(-1, string.Empty, exception.Message);
        }
    }

    private static string FirstLine(string text, string fallback)
    {
        foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0)
            {
                return trimmed;
            }
        }

        return fallback;
    }
}

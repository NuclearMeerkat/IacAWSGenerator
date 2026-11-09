namespace IacAgent.Core;

/// <summary>Runs an external tool such as Terraform or Checkov.</summary>
public interface IProcessRunner
{
    /// <summary>Runs the executable and returns both streams.</summary>
    Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken);
}

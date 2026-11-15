using IacAgent.Core;

namespace IacAgent.Host.Tests;

internal sealed class QuietProcessRunner : IProcessRunner
{
    public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory, CancellationToken cancellationToken)
    {
        var output = fileName == "terraform" ? "Terraform v1.2.3" : "3.3.30";
        return Task.FromResult(new ProcessResult(0, output, string.Empty));
    }
}

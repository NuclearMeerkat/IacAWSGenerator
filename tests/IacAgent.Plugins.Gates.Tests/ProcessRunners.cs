using IacAgent.Core;

namespace IacAgent.Plugins.Gates.Tests;

internal sealed class IdleProcessRunner : IProcessRunner
{
    public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory, CancellationToken cancellationToken)
    {
        return Task.FromResult(new ProcessResult(0, string.Empty, string.Empty));
    }
}

internal sealed class ScriptedProcessRunner(string stdout) : IProcessRunner
{
    public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory, CancellationToken cancellationToken)
    {
        return Task.FromResult(new ProcessResult(1, stdout, string.Empty));
    }
}

internal sealed class ThrowingProcessRunner : IProcessRunner
{
    public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory, CancellationToken cancellationToken)
    {
        throw new InvalidOperationException("The tool was called.");
    }
}

internal sealed class FmtProcessRunner(int fmtExitCode, string fmtError) : IProcessRunner
{
    public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory, CancellationToken cancellationToken)
    {
        if (arguments.Count > 0 && arguments[0] == "fmt")
        {
            return Task.FromResult(new ProcessResult(fmtExitCode, string.Empty, fmtError));
        }

        var stdout = arguments.Contains("validate") ? """{"diagnostics":[]}""" : string.Empty;
        return Task.FromResult(new ProcessResult(0, stdout, string.Empty));
    }
}

internal sealed class TerraformProcessRunner(string validateJson) : IProcessRunner
{
    public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory, CancellationToken cancellationToken)
    {
        var validate = arguments.Contains("validate");
        return Task.FromResult(new ProcessResult(validate ? 1 : 0, validate ? validateJson : string.Empty, string.Empty));
    }
}

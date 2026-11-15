using IacAgent.Core;

namespace IacAgent.Host.Doctor;

/// <summary>Reads a tool version the same way doctor does.</summary>
internal static class ToolVersionReader
{
    public readonly record struct Probe(bool Ok, string Detail);

    public static async Task<Probe> ReadAsync(
        IProcessRunner processRunner,
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var result = await processRunner.RunAsync(fileName, arguments, Path.GetTempPath(), cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            var error = FirstLine(result.StandardError);
            var detail = error.Length == 0 ? $"{fileName} exited with code {result.ExitCode}." : error;
            return new Probe(false, detail);
        }

        var version = FirstLine(result.StandardOutput);
        if (version.Length == 0)
        {
            version = FirstLine(result.StandardError);
        }

        return new Probe(true, version);
    }

    public static string FirstLine(string text)
    {
        foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0)
            {
                return trimmed;
            }
        }

        return string.Empty;
    }
}

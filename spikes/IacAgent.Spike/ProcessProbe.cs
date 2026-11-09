using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace IacAgent.Spike;

internal sealed record ProcessOutput(int ExitCode, string StandardOutput, string StandardError);

/// <summary>Runs an external executable and captures both streams.</summary>
internal static class ProcessProbe
{
    public static async Task<ProcessOutput> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var resolved = Resolve(fileName)
            ?? throw new InvalidOperationException($"'{fileName}' was not found on PATH.");
        var startInfo = CreateStartInfo(resolved, arguments, workingDirectory);

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException($"Could not start '{fileName}'.");
            }
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 2)
        {
            throw new InvalidOperationException($"'{fileName}' was not found on PATH.", exception);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            throw;
        }

        return new ProcessOutput(process.ExitCode, await stdoutTask, await stderrTask);
    }

    private static ProcessStartInfo CreateStartInfo(
        string resolvedPath,
        IReadOnlyList<string> arguments,
        string workingDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        var extension = Path.GetExtension(resolvedPath);
        var launchThroughCmd = extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".bat", StringComparison.OrdinalIgnoreCase);
        if (launchThroughCmd)
        {
            startInfo.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            startInfo.ArgumentList.Add("/d");
            startInfo.ArgumentList.Add("/s");
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add(JoinCommand(resolvedPath, arguments));
            return startInfo;
        }

        startInfo.FileName = resolvedPath;
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static string? Resolve(string fileName)
    {
        if (Path.IsPathRooted(fileName) || fileName.Contains(Path.DirectorySeparatorChar) || fileName.Contains(Path.AltDirectorySeparatorChar))
        {
            return File.Exists(fileName) ? fileName : null;
        }

        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        var extensions = Environment.GetEnvironmentVariable("PATHEXT")?.Split(';', StringSplitOptions.RemoveEmptyEntries)
            ?? [".exe", ".cmd", ".bat"];
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = directory.Trim();
            if (Path.HasExtension(fileName))
            {
                var exact = Path.Combine(trimmed, fileName);
                if (File.Exists(exact))
                {
                    return exact;
                }

                continue;
            }

            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(trimmed, fileName + extension);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static string JoinCommand(string fileName, IReadOnlyList<string> arguments)
    {
        var builder = new StringBuilder(Quote(fileName));
        foreach (var argument in arguments)
        {
            builder.Append(' ');
            builder.Append(Quote(argument));
        }

        return builder.ToString();
    }

    private static string Quote(string value)
    {
        if (value.Contains('"') || value.Contains(' '))
        {
            return "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
        }

        return value;
    }
}

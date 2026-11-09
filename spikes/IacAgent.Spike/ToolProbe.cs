using System.Text.Json;

namespace IacAgent.Spike;

/// <summary>Runs Terraform validate and Checkov, then prints structured issues.</summary>
internal static class ToolProbe
{
    public static async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var failures = 0;
        failures += await RunTerraformAsync("valid", expectIssues: false, initialize: true, cancellationToken) ? 0 : 1;
        // Terraform 1.16 rejects init when the configuration does not parse. validate -json still returns diagnostics.
        failures += await RunTerraformAsync("invalid", expectIssues: true, initialize: false, cancellationToken) ? 0 : 1;
        failures += await RunCheckovAsync(cancellationToken) ? 0 : 1;
        return failures;
    }

    private static async Task<bool> RunTerraformAsync(
        string fixtureName,
        bool expectIssues,
        bool initialize,
        CancellationToken cancellationToken)
    {
        var directory = FixtureDirectory(fixtureName);
        try
        {
            if (initialize)
            {
                var init = await ProcessProbe.RunAsync(
                    "terraform",
                    ["init", "-backend=false", "-input=false"],
                    directory,
                    cancellationToken);
                if (init.ExitCode != 0)
                {
                    Console.WriteLine($"terraform init ({fixtureName}): exit {init.ExitCode}");
                    WriteStream(init.StandardError);
                    return false;
                }
            }

            var validate = await ProcessProbe.RunAsync(
                "terraform",
                ["validate", "-json"],
                directory,
                cancellationToken);
            var issues = TerraformValidateParser.Parse(validate.StandardOutput);
            ToolReport.Print($"terraform validate ({fixtureName})", issues);
            if (expectIssues && issues.Count == 0)
            {
                Console.WriteLine($"terraform validate ({fixtureName}): expected at least one diagnostic.");
                return false;
            }

            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or JsonException)
        {
            Console.WriteLine($"terraform ({fixtureName}): {exception.Message}");
            return false;
        }
    }

    private static async Task<bool> RunCheckovAsync(CancellationToken cancellationToken)
    {
        var directory = FixtureDirectory("insecure");
        try
        {
            var checkov = await ProcessProbe.RunAsync(
                "checkov",
                ["-d", ".", "--framework", "terraform", "-o", "json", "--quiet"],
                directory,
                cancellationToken);
            var issues = CheckovOutputParser.Parse(checkov.StandardOutput);
            ToolReport.Print("checkov (insecure)", issues);
            if (issues.Count == 0)
            {
                Console.WriteLine("checkov (insecure): expected at least one failed check.");
                WriteStream(checkov.StandardError);
                return false;
            }

            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or JsonException)
        {
            Console.WriteLine($"checkov: {exception.Message}");
            return false;
        }
    }

    private static string FixtureDirectory(string name)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "fixtures", name);
        if (!Directory.Exists(directory))
        {
            throw new InvalidOperationException($"Fixture directory '{directory}' is missing.");
        }

        return directory;
    }

    private static void WriteStream(string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            Console.WriteLine(text.Trim());
        }
    }
}

namespace IacAgent.Spike;

/// <summary>Parses saved tool output so the spike can be checked without the binaries.</summary>
internal static class SampleProbe
{
    public static int Run()
    {
        var failures = 0;
        failures += CheckTerraformSample() ? 0 : 1;
        failures += CheckCheckovSample() ? 0 : 1;
        return failures;
    }

    private static bool CheckTerraformSample()
    {
        var issues = TerraformValidateParser.Parse(ReadSample("terraform-validate.json"));
        ToolReport.Print("sample terraform validate", issues);
        var issue = issues.Count == 1 ? issues[0] : null;
        if (issue is { Line: 2, Severity: "error" })
        {
            return true;
        }

        Console.WriteLine("sample terraform validate: parsed shape did not match the saved report.");
        return false;
    }

    private static bool CheckCheckovSample()
    {
        var issues = CheckovOutputParser.Parse(ReadSample("checkov.json"));
        ToolReport.Print("sample checkov", issues);
        var issue = issues.Count == 1 ? issues[0] : null;
        if (issue is { RuleId: "CKV_AWS_24", Severity: "HIGH", Line: 4 })
        {
            return true;
        }

        Console.WriteLine("sample checkov: parsed shape did not match the saved report.");
        return false;
    }

    private static string ReadSample(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "samples", fileName);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Sample '{path}' is missing.");
        }

        return File.ReadAllText(path);
    }
}

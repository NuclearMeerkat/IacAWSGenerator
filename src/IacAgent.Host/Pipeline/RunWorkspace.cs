namespace IacAgent.Host.Pipeline;

/// <summary>Keeps one run's files away from the output directory until the run finishes.</summary>
internal static class RunWorkspace
{
    public static string Create(string runId)
    {
        var workspace = Path.Combine(Path.GetTempPath(), "iacagent", runId);
        Directory.CreateDirectory(workspace);
        return workspace;
    }

    public static void Publish(string workspace, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        foreach (var relative in ReadFiles(Path.Combine(outputDirectory, "run.json")))
        {
            DeleteInside(outputDirectory, relative);
        }

        CopyIfExists(workspace, outputDirectory, "run.json");
        CopyIfExists(workspace, outputDirectory, "escalation.md");
        CopyIfExists(workspace, outputDirectory, "review.md");
        foreach (var relative in ReadFiles(Path.Combine(workspace, "run.json")))
        {
            CopyIfExists(workspace, outputDirectory, relative);
        }
    }

    public static void Delete(string workspace)
    {
        if (Directory.Exists(workspace))
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    private static IReadOnlyList<string> ReadFiles(string runJsonPath)
    {
        if (!File.Exists(runJsonPath))
        {
            return [];
        }

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(runJsonPath));
            if (!document.RootElement.TryGetProperty("files", out var files) || files.ValueKind != System.Text.Json.JsonValueKind.Array)
            {
                return [];
            }

            return files.EnumerateArray().Select(item => item.GetString() ?? string.Empty).Where(path => path.Length > 0).ToList();
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }
    }

    private static void CopyIfExists(string workspace, string outputDirectory, string relative)
    {
        if (relative is "run.json" or "escalation.md" or "review.md")
        {
            var source = Path.Combine(workspace, relative);
            if (File.Exists(source))
            {
                File.Copy(source, Path.Combine(outputDirectory, relative), overwrite: true);
            }

            return;
        }

        if (!TryInside(workspace, relative, out var sourcePath) || !File.Exists(sourcePath) || !TryInside(outputDirectory, relative, out var destination))
        {
            return;
        }

        var parent = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        File.Copy(sourcePath, destination, overwrite: true);
    }

    private static void DeleteInside(string root, string relative)
    {
        if (TryInside(root, relative, out var path) && File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static bool TryInside(string root, string relative, out string fullPath)
    {
        var normalized = relative.Replace('\\', '/').TrimStart('/');
        if (normalized.Contains("..", StringComparison.Ordinal)
            || Path.IsPathRooted(normalized)
            || normalized.Equals(".terraform.lock.hcl", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith(".terraform/", StringComparison.OrdinalIgnoreCase))
        {
            fullPath = string.Empty;
            return false;
        }

        fullPath = Path.GetFullPath(Path.Combine(root, normalized));
        var prefix = Path.GetFullPath(root + Path.DirectorySeparatorChar);
        return fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}

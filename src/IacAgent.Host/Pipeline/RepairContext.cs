using IacAgent.Core;
using IacAgent.Host.Configuration;
using IacAgent.Host.Recording;

namespace IacAgent.Host.Pipeline;

/// <summary>Builds the developer context for a first write or a repair.</summary>
internal static class RepairContext
{
    public static AgentContext ForDeveloper(
        GenerateRequest request,
        string directory,
        string? requirements,
        string? designNotes,
        IReadOnlyList<GeneratedFile> files,
        IReadOnlyList<GateResult> results,
        int repairs)
    {
        var failing = repairs == 0 ? null : FailingFiles(files, results);
        return new AgentContext(
            request.Prompt,
            request.AutoApprove,
            requirements,
            failing is null ? null : new Artifact(directory, failing),
            repairs == 0 ? null : results,
            OtherFiles(files, failing),
            designNotes);
    }

    private static IReadOnlyList<string>? OtherFiles(IReadOnlyList<GeneratedFile> files, IReadOnlyList<GeneratedFile>? failing)
    {
        if (failing is null)
        {
            return null;
        }

        var paths = failing.Select(file => file.RelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var lines = files.Where(file => !paths.Contains(file.RelativePath)).Select(TerraformResourceInventory.Describe).ToList();
        return lines.Count == 0 ? null : lines;
    }

    private static IReadOnlyList<GeneratedFile> FailingFiles(IReadOnlyList<GeneratedFile> files, IReadOnlyList<GateResult> results)
    {
        var names = results
            .SelectMany(result => result.Issues)
            .Where(issue => issue.IsBlocking)
            .Select(issue => issue.File)
            .Where(file => !string.IsNullOrWhiteSpace(file))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (names.Count == 0)
        {
            return files;
        }

        var matched = files.Where(file => names.Contains(file.RelativePath) || names.Contains(Path.GetFileName(file.RelativePath))).ToList();
        return matched.Count == 0 ? files : matched;
    }
}

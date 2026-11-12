using IacAgent.Core;

namespace IacAgent.Host.Pipeline;

internal static class WorkspaceFiles
{
    /// <summary>Keeps earlier files and replaces only the paths present in the update.</summary>
    public static List<GeneratedFile> Merge(IReadOnlyList<GeneratedFile> current, IReadOnlyList<GeneratedFile> updates)
    {
        var merged = new Dictionary<string, GeneratedFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in current)
        {
            merged[Key(file.RelativePath)] = file;
        }

        foreach (var file in updates)
        {
            merged[Key(file.RelativePath)] = file;
        }

        return merged.Values.ToList();
    }

    public static void ReplaceTerraformFiles(string directory, IReadOnlyList<GeneratedFile> files)
    {
        Directory.CreateDirectory(directory);
        foreach (var existing in Directory.EnumerateFiles(directory, "*.tf"))
        {
            File.Delete(existing);
        }

        foreach (var file in files)
        {
            var relative = file.RelativePath.Replace('\\', '/').TrimStart('/');
            if (relative.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(file.RelativePath))
            {
                throw new InvalidOperationException($"Refusing to write '{file.RelativePath}' outside the output directory.");
            }

            if (relative.EndsWith(".tfstate", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var destination = Path.GetFullPath(Path.Combine(directory, relative));
            var root = Path.GetFullPath(directory + Path.DirectorySeparatorChar);
            if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Refusing to write '{file.RelativePath}' outside the output directory.");
            }

            var parent = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            File.WriteAllText(destination, file.Content);
        }
    }

    private static string Key(string path)
    {
        return path.Replace('\\', '/').TrimStart('/');
    }
}

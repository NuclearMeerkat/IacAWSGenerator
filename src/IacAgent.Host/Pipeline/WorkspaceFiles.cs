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
            var destination = Resolve(directory, file.RelativePath);
            if (destination is null)
            {
                throw new InvalidOperationException($"Refusing to write '{file.RelativePath}' outside the output directory.");
            }

            if (Normalize(file.RelativePath).EndsWith(".tfstate", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parent = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            File.WriteAllText(destination, file.Content);
        }
    }

    /// <summary>Reads the generated files back from disk, keeping the original content when a path is unsafe or missing.</summary>
    public static IReadOnlyList<GeneratedFile> Read(string directory, IReadOnlyList<GeneratedFile> files)
    {
        return files.Select(file => Read(directory, file)).ToList();
    }

    private static GeneratedFile Read(string directory, GeneratedFile file)
    {
        var path = Resolve(directory, file.RelativePath);
        if (path is null || !File.Exists(path))
        {
            return file;
        }

        return file with { Content = File.ReadAllText(path) };
    }

    private static string? Resolve(string directory, string relativePath)
    {
        var relative = Normalize(relativePath);
        if (relative.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(relativePath))
        {
            return null;
        }

        var destination = Path.GetFullPath(Path.Combine(directory, relative));
        var root = Path.GetFullPath(directory + Path.DirectorySeparatorChar);
        if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return destination;
    }

    private static string Normalize(string path)
    {
        return path.Replace('\\', '/').TrimStart('/');
    }

    private static string Key(string path)
    {
        return Normalize(path);
    }
}

using IacAgent.Core;

namespace IacAgent.Host.Pipeline;

internal static class WorkspaceFiles
{
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
}

using System.Text.RegularExpressions;

using IacAgent.Core;

namespace IacAgent.Host.Recording;

/// <summary>Counts top-level Terraform resource blocks. It is not an HCL parser.</summary>
internal static partial class TerraformResourceInventory
{
    public static Result Count(IEnumerable<GeneratedFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            if (!file.RelativePath.EndsWith(".tf", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (Match match in ResourceDeclaration().Matches(StripComments(file.Content)))
            {
                var type = match.Groups["type"].Value;
                counts[type] = counts.TryGetValue(type, out var current) ? current + 1 : 1;
            }
        }

        return new Result(counts.Values.Sum(), counts);
    }

    /// <summary>One line: the path, then each <c>type.name</c> the file declares.</summary>
    public static string Describe(GeneratedFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (!file.RelativePath.EndsWith(".tf", StringComparison.OrdinalIgnoreCase))
        {
            return file.RelativePath;
        }

        var names = new List<string>();
        foreach (Match match in ResourceDeclaration().Matches(StripComments(file.Content)))
        {
            names.Add(match.Groups["type"].Value + "." + match.Groups["name"].Value);
        }

        return names.Count == 0 ? file.RelativePath : file.RelativePath + ": " + string.Join(", ", names);
    }

    private static string StripComments(string text)
    {
        var withoutBlocks = BlockComment().Replace(text, string.Empty);
        var lines = withoutBlocks.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            lines[index] = StripLineComment(lines[index]);
        }

        return string.Join('\n', lines);
    }

    private static string StripLineComment(string line)
    {
        var slash = line.IndexOf("//", StringComparison.Ordinal);
        if (slash >= 0)
        {
            line = line[..slash];
        }

        var hash = line.IndexOf('#');
        if (hash >= 0)
        {
            line = line[..hash];
        }

        return line;
    }

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex BlockComment();

    [GeneratedRegex(@"\bresource\s+""(?<type>[A-Za-z0-9_]+)""\s+""(?<name>[A-Za-z0-9_]+)""", RegexOptions.CultureInvariant)]
    private static partial Regex ResourceDeclaration();

    internal sealed record Result(int Count, IReadOnlyDictionary<string, int> Types);
}

namespace IacAgent.Plugins.Gates.Tests;

/// <summary>Skips the test when the named tool is not on PATH.</summary>
public sealed class RequiresToolFactAttribute : FactAttribute
{
    public RequiresToolFactAttribute(string fileName)
    {
        if (!IsOnPath(fileName))
        {
            Skip = $"{fileName} is not on PATH.";
        }
    }

    private static bool IsOnPath(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var extensions = Environment.GetEnvironmentVariable("PATHEXT")?.Split(';', StringSplitOptions.RemoveEmptyEntries)
            ?? [".exe", ".cmd", ".bat"];
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (File.Exists(Path.Combine(directory, fileName)))
            {
                return true;
            }

            foreach (var extension in extensions)
            {
                if (File.Exists(Path.Combine(directory, fileName + extension)))
                {
                    return true;
                }
            }
        }

        return false;
    }
}

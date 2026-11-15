using System.Globalization;
using System.Text;

namespace IacAgent.Host.Benchmark;

internal static class BenchmarkCsv
{
    public static string Format(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine("task,profile,strongModel,cheapModel,temperature,security,maxRetries,repeat,outcome,iterations,inputTokens,outputTokens,cost,duration,resourceCount,blockingIssues,advisoryIssues,intentMet,missingGroups");
        foreach (var row in rows)
        {
            builder.AppendLine(string.Join(',', row.Select(Quote)));
        }

        return builder.ToString();
    }

    private static string Quote(string value)
    {
        if (value.IndexOfAny([',', '"', '\r', '\n']) < 0)
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}

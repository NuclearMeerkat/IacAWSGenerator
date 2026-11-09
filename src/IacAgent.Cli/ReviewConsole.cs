using IacAgent.Host.Review;

using Spectre.Console;

namespace IacAgent.Cli;

internal static class ReviewConsole
{
    public static void Write(PlanReview review)
    {
        if (review.Findings.Count == 0)
        {
            AnsiConsole.MarkupLine("[green]Checkov reported no findings.[/]");
        }
        else
        {
            AnsiConsole.MarkupLine("[yellow]Checkov findings:[/]");
            foreach (var finding in review.Findings)
            {
                var line = finding.Line?.ToString() ?? "-";
                AnsiConsole.WriteLine($"  {finding.File}:{line} [{finding.Severity}] {finding.RuleId}: {finding.Message}");
            }
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[yellow]Explanation:[/]");
        AnsiConsole.WriteLine(review.Explanation);
    }
}

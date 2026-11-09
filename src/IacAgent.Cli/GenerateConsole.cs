using IacAgent.Core;

using Spectre.Console;

namespace IacAgent.Cli;

internal static class GenerateConsole
{
    public static void Write(RunOutcome outcome, string outputDirectory)
    {
        var runPath = Path.Combine(outputDirectory, "run.json");
        if (outcome == RunOutcome.Succeeded)
        {
            AnsiConsole.MarkupLine("[green]Gates passed.[/]");
        }
        else if (outcome == RunOutcome.ConfigError)
        {
            AnsiConsole.MarkupLine("[red]The profile is not registered.[/]");
        }
        else if (outcome == RunOutcome.BudgetExceeded)
        {
            AnsiConsole.MarkupLine("[red]The design is over the monthly budget.[/] See escalation.md.");
        }
        else if (outcome == RunOutcome.NeedsHuman)
        {
            AnsiConsole.MarkupLine("[yellow]A decision needs a person.[/] Assumptions are in run.json.");
        }
        else
        {
            AnsiConsole.MarkupLine("[red]The run stopped before the gates passed.[/] See escalation.md.");
        }

        AnsiConsole.WriteLine($"Outcome: {outcome}");
        AnsiConsole.WriteLine($"run.json: {runPath}");
    }
}

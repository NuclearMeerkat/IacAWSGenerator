using IacAgent.Host.Doctor;

using Spectre.Console;

namespace IacAgent.Cli;

internal static class DoctorConsole
{
    public static void Write(DoctorReport report)
    {
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Check");
        table.AddColumn("Result");
        table.AddColumn("Detail");
        foreach (var check in report.Checks)
        {
            var result = check.Passed ? "ok" : check.Required ? "missing" : "optional";
            var detail = check.Fix is null ? check.Detail : $"{check.Detail} Fix: {check.Fix}";
            table.AddRow(Markup.Escape(check.Name), result, Markup.Escape(detail));
        }

        AnsiConsole.Write(table);
        if (!report.Succeeded)
        {
            AnsiConsole.MarkupLine("[red]Required prerequisites are missing.[/]");
        }
    }
}

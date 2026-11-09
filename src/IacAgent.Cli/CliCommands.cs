using System.CommandLine;

using IacAgent.Host.Configuration;

namespace IacAgent.Cli;

internal static class CliCommands
{
    public static RootCommand Create(
        Func<ParseResult, CancellationToken, Task<int>> generate,
        Func<ParseResult, CancellationToken, Task<int>> doctor,
        Func<ParseResult, CancellationToken, Task<int>> review)
    {
        var root = new RootCommand("Generate AWS Terraform from a prompt. Agents propose code. Gates accept it.");
        root.Subcommands.Add(CreateGenerate(generate));
        root.Subcommands.Add(CreateDoctor(doctor));
        root.Subcommands.Add(CreateReview(review));
        return root;
    }

    public static SettingsOverlay ReadGenerate(ParseResult parseResult)
    {
        var command = (Command)parseResult.CommandResult.Command;
        return new SettingsOverlay
        {
            Profile = ReadString(parseResult, Find<string>(command, "--profile")),
            OutputDirectory = ReadString(parseResult, Find<string>(command, "--out")),
            ModelStrong = ReadString(parseResult, Find<string>(command, "--model-strong")),
            ModelCheap = ReadString(parseResult, Find<string>(command, "--model-cheap")),
            Endpoint = ReadString(parseResult, Find<string>(command, "--endpoint")),
            MaxRepairIterations = ReadInt(parseResult, Find<int>(command, "--max-retries")),
            AutoApprove = ReadBool(parseResult, Find<bool>(command, "--auto-approve")),
            Verbose = ReadBool(parseResult, Find<bool>(command, "--verbose")),
            Budget = ReadString(parseResult, Find<string>(command, "--budget")),
        };
    }

    public static SettingsOverlay ReadDoctor(ParseResult parseResult)
    {
        var command = (Command)parseResult.CommandResult.Command;
        return new SettingsOverlay
        {
            Endpoint = ReadString(parseResult, Find<string>(command, "--endpoint")),
            Verbose = ReadBool(parseResult, Find<bool>(command, "--verbose")),
        };
    }

    public static SettingsOverlay ReadReview(ParseResult parseResult)
    {
        var command = (Command)parseResult.CommandResult.Command;
        return new SettingsOverlay
        {
            ModelCheap = ReadString(parseResult, Find<string>(command, "--model-cheap")),
            Endpoint = ReadString(parseResult, Find<string>(command, "--endpoint")),
            Verbose = ReadBool(parseResult, Find<bool>(command, "--verbose")),
        };
    }

    public static (string? Plan, string? Source) ReadReviewPaths(ParseResult parseResult)
    {
        var command = (Command)parseResult.CommandResult.Command;
        return (parseResult.GetValue(Find<string>(command, "--plan")), ReadString(parseResult, Find<string>(command, "--source")));
    }

    public static string ReadPrompt(ParseResult parseResult)
    {
        var command = (Command)parseResult.CommandResult.Command;
        var argument = command.Arguments.OfType<Argument<string>>().Single();
        return parseResult.GetValue(argument) ?? string.Empty;
    }

    private static Command CreateGenerate(Func<ParseResult, CancellationToken, Task<int>> generate)
    {
        var command = new Command("generate", "Run a profile, the gates, and write run.json.");
        command.Arguments.Add(new Argument<string>("prompt") { Description = "What to generate." });
        command.Options.Add(new Option<string>("--profile") { Description = "Profile name. Defaults to trio." });
        command.Options.Add(new Option<string>("--out") { Description = "Output directory. Defaults to ./out." });
        command.Options.Add(new Option<string>("--model-strong") { Description = "Strong-tier model id." });
        command.Options.Add(new Option<string>("--model-cheap") { Description = "Cheap-tier model id." });
        command.Options.Add(new Option<string>("--endpoint") { Description = "Local model server. Omit this to use OpenRouter." });
        command.Options.Add(new Option<int>("--max-retries") { Description = "Repair limit. Defaults to 3." });
        command.Options.Add(new Option<string>("--budget") { Description = "Maximum estimated cost in USD per month." });
        command.Options.Add(new Option<bool>("--auto-approve") { Description = "Skip interactive questions." });
        command.Options.Add(new Option<bool>("--verbose") { Description = "Write debug logs." });
        command.SetAction((parseResult, cancellationToken) => generate(parseResult, cancellationToken));
        return command;
    }

    private static Command CreateReview(Func<ParseResult, CancellationToken, Task<int>> review)
    {
        var command = new Command("review", "Explain a Terraform plan and the Checkov findings in its source.");
        command.Options.Add(new Option<string>("--plan") { Description = "terraform show -json output.", Required = true });
        command.Options.Add(new Option<string>("--source") { Description = "Terraform directory. Defaults to the plan file's directory." });
        command.Options.Add(new Option<string>("--model-cheap") { Description = "Cheap-tier model id." });
        command.Options.Add(new Option<string>("--endpoint") { Description = "Local model server. Omit this to use OpenRouter." });
        command.Options.Add(new Option<bool>("--verbose") { Description = "Write debug logs." });
        command.SetAction((parseResult, cancellationToken) => review(parseResult, cancellationToken));
        return command;
    }

    private static Command CreateDoctor(Func<ParseResult, CancellationToken, Task<int>> doctor)
    {
        var command = new Command("doctor", "Check Terraform, Checkov, Infracost, and model access.");
        command.Options.Add(new Option<string>("--endpoint") { Description = "Local model server. Omit this to check OpenRouter." });
        command.Options.Add(new Option<bool>("--verbose") { Description = "Write debug logs." });
        command.SetAction((parseResult, cancellationToken) => doctor(parseResult, cancellationToken));
        return command;
    }

    private static Option<T> Find<T>(Command command, string name)
    {
        return command.Options.OfType<Option<T>>().Single(option => option.Name == name || option.Aliases.Contains(name));
    }

    private static string? ReadString(ParseResult parseResult, Option<string> option)
    {
        return IsSpecified(parseResult, option) ? parseResult.GetValue(option) : null;
    }

    private static int? ReadInt(ParseResult parseResult, Option<int> option)
    {
        return IsSpecified(parseResult, option) ? parseResult.GetValue(option) : null;
    }

    private static bool? ReadBool(ParseResult parseResult, Option<bool> option)
    {
        return IsSpecified(parseResult, option) ? parseResult.GetValue(option) : null;
    }

    private static bool IsSpecified(ParseResult parseResult, Option option)
    {
        return parseResult.GetResult(option) is not null;
    }
}

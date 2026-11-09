using IacAgent.Core;

using Spectre.Console;

namespace IacAgent.Cli;

/// <summary>Asks with Spectre when the console can accept input.</summary>
internal sealed class ConsoleUserInteraction : IUserInteraction
{
    /// <inheritdoc />
    public Task<string?> AskAsync(string question, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        cancellationToken.ThrowIfCancellationRequested();
        if (!AnsiConsole.Profile.Capabilities.Interactive)
        {
            return Task.FromResult<string?>(null);
        }

        return Task.FromResult<string?>(AnsiConsole.Ask<string>(Markup.Escape(question)));
    }
}

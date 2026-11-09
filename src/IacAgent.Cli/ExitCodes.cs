using IacAgent.Core;

namespace IacAgent.Cli;

/// <summary>Process exit codes for iacagent.</summary>
public static class ExitCodes
{
    /// <summary>The run finished and the gates passed.</summary>
    public const int Success = 0;

    /// <summary>An unexpected failure.</summary>
    public const int Unexpected = 1;

    /// <summary>A gate failed after the allowed retries.</summary>
    public const int GateFailed = 10;

    /// <summary>The design is over the budget.</summary>
    public const int BudgetExceeded = 11;

    /// <summary>The run needs a human decision.</summary>
    public const int NeedsHuman = 12;

    /// <summary>Configuration or a required prerequisite is missing.</summary>
    public const int ConfigError = 20;

    /// <summary>Maps a pipeline outcome to an exit code.</summary>
    public static int For(RunOutcome outcome)
    {
        return outcome switch
        {
            RunOutcome.Succeeded => Success,
            RunOutcome.GateFailed => GateFailed,
            RunOutcome.BudgetExceeded => BudgetExceeded,
            RunOutcome.NeedsHuman => NeedsHuman,
            RunOutcome.ConfigError => ConfigError,
            _ => Unexpected,
        };
    }
}

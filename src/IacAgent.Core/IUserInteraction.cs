namespace IacAgent.Core;

/// <summary>Asks the user one question. Returns null when the run must not prompt.</summary>
public interface IUserInteraction
{
    /// <summary>Asks a question and returns the answer, or null when prompting is unavailable.</summary>
    Task<string?> AskAsync(string question, CancellationToken cancellationToken);
}

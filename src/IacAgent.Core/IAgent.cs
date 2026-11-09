namespace IacAgent.Core;

/// <summary>One step that proposes or reviews infrastructure code.</summary>
public interface IAgent
{
    /// <summary>Gets the agent name recorded in logs.</summary>
    string Name { get; }

    /// <summary>Gets the agent's place in the generate flow.</summary>
    AgentRole Role { get; }

    /// <summary>Runs the agent once.</summary>
    Task<AgentResult> RunAsync(AgentContext context, CancellationToken cancellationToken);
}

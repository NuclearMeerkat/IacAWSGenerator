using IacAgent.Core;

namespace IacAgent.Plugins.Agents.Solo;

/// <summary>One developer that gathers requirements, applies the security rules, and writes Terraform.</summary>
internal sealed class SoloDeveloperAgent(IModelRouter router, IEnumerable<IRedactionRule> rules) : IAgent
{
    private readonly string _prompt = ChatText.ReadPrompt("Solo.md");

    /// <inheritdoc />
    public string Name => "solo-developer";

    /// <inheritdoc />
    public AgentRole Role => AgentRole.Developer;

    /// <inheritdoc />
    public Task<AgentResult> RunAsync(AgentContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return TerraformReply.WriteAsync(router.GetClient(ModelTier.Strong), _prompt, TerraformReply.UserMessage(context), rules, cancellationToken);
    }
}

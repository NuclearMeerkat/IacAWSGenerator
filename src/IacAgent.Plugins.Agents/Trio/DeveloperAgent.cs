using IacAgent.Core;

namespace IacAgent.Plugins.Agents.Trio;

/// <summary>Writes Terraform files as JSON and retries once when the JSON is invalid.</summary>
internal sealed class DeveloperAgent(IModelRouter router, IEnumerable<IRedactionRule> rules) : IAgent
{
    private readonly string _prompt = ChatText.ReadPrompt("Developer.md");

    /// <inheritdoc />
    public string Name => "developer";

    /// <inheritdoc />
    public AgentRole Role => AgentRole.Developer;

    /// <inheritdoc />
    public Task<AgentResult> RunAsync(AgentContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return TerraformReply.WriteAsync(router.GetClient(ModelTier.Strong), _prompt, TerraformReply.UserMessage(context), rules, cancellationToken);
    }
}

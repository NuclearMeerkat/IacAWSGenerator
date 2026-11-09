using IacAgent.Core;

namespace IacAgent.Plugins.Agents.Full;

/// <summary>Calls one model tier and returns the reply as design notes.</summary>
internal class AdviceAgent(IModelRouter router, IEnumerable<IRedactionRule> rules, string name, string promptFile, ModelTier tier) : IAgent
{
    private readonly string _prompt = ChatText.ReadPrompt(promptFile);

    /// <inheritdoc />
    public string Name => name;

    /// <inheritdoc />
    public AgentRole Role => AgentRole.Advisor;

    /// <inheritdoc />
    public async Task<AgentResult> RunAsync(AgentContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var user = string.IsNullOrWhiteSpace(context.RequirementsJson) ? context.Prompt : context.RequirementsJson;
        var (text, tokens) = await ChatText.CompleteAsync(router.GetClient(tier), _prompt, user, rules, cancellationToken).ConfigureAwait(false);
        return new AgentResult([], Review: text.Trim(), Tokens: tokens);
    }
}

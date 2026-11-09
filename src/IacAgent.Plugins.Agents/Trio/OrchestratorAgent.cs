using System.Text.Json;

using IacAgent.Core;

namespace IacAgent.Plugins.Agents.Trio;

/// <summary>Turns a prompt into requirements JSON.</summary>
internal sealed class OrchestratorAgent(IModelRouter router, IEnumerable<IRedactionRule> rules, IUserInteraction interaction) : IAgent
{
    private readonly string _prompt = ChatText.ReadPrompt("Orchestrator.md");

    /// <inheritdoc />
    public string Name => "orchestrator";

    /// <inheritdoc />
    public AgentRole Role => AgentRole.Orchestrator;

    /// <inheritdoc />
    public async Task<AgentResult> RunAsync(AgentContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var client = router.GetClient(ModelTier.Strong);
        var user = $"Auto-approve: {context.AutoApprove}\n\n{context.Prompt}";
        var (text, tokens) = await ChatText.CompleteAsync(client, _prompt, user, rules, cancellationToken).ConfigureAwait(false);
        var document = ChatText.ParseObject(text);
        if (document is null)
        {
            (text, var retryTokens) = await ChatText.CompleteAsync(client, _prompt, "Reply with only the JSON object.\n\n" + text, rules, cancellationToken).ConfigureAwait(false);
            tokens = new TokenCount(tokens.Input + retryTokens.Input, tokens.Output + retryTokens.Output);
            document = ChatText.ParseObject(text);
        }

        if (document is null)
        {
            return new AgentResult([], Tokens: tokens, Failed: true, FailureMessage: "The orchestrator did not return JSON.");
        }

        using (document)
        {
            var assumptions = ReadStrings(document.RootElement, "assumptions");
            var questions = ReadStrings(document.RootElement, "questions");
            var needsPerson = ReadNeedsHuman(document.RootElement);
            var unanswered = await CollectAnswersAsync(context, questions, assumptions, interaction, cancellationToken).ConfigureAwait(false);
            var requirements = document.RootElement.TryGetProperty("requirements", out var body)
                ? body.GetRawText()
                : document.RootElement.GetRawText();
            if (needsPerson && (context.AutoApprove || unanswered))
            {
                return new AgentResult(assumptions, requirements, Tokens: tokens, NeedsHuman: true, FailureMessage: "A decision needs a person.");
            }

            return new AgentResult(assumptions, requirements, Tokens: tokens);
        }
    }

    private static async Task<bool> CollectAnswersAsync(
        AgentContext context,
        IReadOnlyList<string> questions,
        List<string> assumptions,
        IUserInteraction interaction,
        CancellationToken cancellationToken)
    {
        var unanswered = false;
        if (context.AutoApprove)
        {
            assumptions.AddRange(questions.Select(question => $"Assumed a default for: {question}"));
            return false;
        }

        foreach (var question in questions)
        {
            var answer = await interaction.AskAsync(question, cancellationToken).ConfigureAwait(false);
            if (answer is null)
            {
                unanswered = true;
                assumptions.Add($"Assumed a default for: {question}");
            }
            else
            {
                assumptions.Add($"{question} {answer}");
            }
        }

        return unanswered;
    }

    private static bool ReadNeedsHuman(JsonElement element)
    {
        return element.TryGetProperty("needsHuman", out var flag) && flag.ValueKind == JsonValueKind.True;
    }

    private static List<string> ReadStrings(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return array.EnumerateArray().Select(item => item.GetString()).Where(item => !string.IsNullOrWhiteSpace(item)).Cast<string>().ToList();
    }
}

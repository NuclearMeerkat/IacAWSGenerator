using System.Text;
using System.Text.Json;

using IacAgent.Core;

namespace IacAgent.Plugins.Agents.Trio;

/// <summary>Turns a prompt into requirements JSON, then revises it from the user's answers.</summary>
internal sealed class OrchestratorAgent(IModelRouter router, IEnumerable<IRedactionRule> rules, IUserInteraction interaction) : IAgent
{
    private const int MaxClarificationRounds = 3;
    private static readonly string[] TextPropertyNames = ["question", "text", "assumption", "message", "content"];
    private readonly string _prompt = ChatText.ReadPrompt("Orchestrator.md");

    /// <inheritdoc />
    public string Name => KnownGates.Orchestrator;

    /// <inheritdoc />
    public AgentRole Role => AgentRole.Orchestrator;

    /// <inheritdoc />
    public async Task<AgentResult> RunAsync(AgentContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var call = ChatCall.For(router, ModelTier.Strong);
        var conversation = new List<Exchange>();
        var assumed = new List<string>();
        var unanswered = false;
        var tokens = new TokenCount(0, 0);
        OrchestratorReply? reply = null;

        for (var round = 0; round <= MaxClarificationRounds; round++)
        {
            var finalRound = round == MaxClarificationRounds;
            var asked = await AskModelAsync(call, BuildFollowUpPrompt(context, conversation, finalRound), cancellationToken).ConfigureAwait(false);
            tokens = tokens.Plus(asked.Tokens);
            if (asked.Reply is null)
            {
                return new AgentResult([], Tokens: tokens, Failed: true, FailureMessage: "The orchestrator did not return JSON.");
            }

            reply = asked.Reply;
            if (reply.Questions.Count == 0)
            {
                break;
            }

            if (context.AutoApprove || finalRound)
            {
                assumed.AddRange(reply.Questions);
                break;
            }

            var answers = await AskUserAsync(reply.Advice, reply.Questions, cancellationToken).ConfigureAwait(false);
            conversation.AddRange(answers.Exchanges);
            if (answers.Unanswered.Count > 0)
            {
                unanswered = true;
                assumed.AddRange(answers.Unanswered);
                break;
            }

            if (answers.AllEmpty)
            {
                break;
            }
        }

        return Finish(context, reply!, conversation, assumed, unanswered, tokens);
    }

    private async Task<ModelTurn> AskModelAsync(ChatCall call, string user, CancellationToken cancellationToken)
    {
        var (text, tokens) = await ChatText.CompleteAsync(call, _prompt, user, rules, cancellationToken).ConfigureAwait(false);
        var reply = ReadReply(text);
        if (reply is not null)
        {
            return new ModelTurn(reply, tokens);
        }

        (text, var retryTokens) = await ChatText.CompleteAsync(call, _prompt, "Reply with only the JSON object.\n\n" + text, rules, cancellationToken).ConfigureAwait(false);
        return new ModelTurn(ReadReply(text), tokens.Plus(retryTokens));
    }

    private async Task<UserRound> AskUserAsync(string advice, IReadOnlyList<string> questions, CancellationToken cancellationToken)
    {
        var exchanges = new List<Exchange>();
        for (var index = 0; index < questions.Count; index++)
        {
            var answer = await interaction.AskAsync(ShownQuestion(advice, questions[index], index), cancellationToken).ConfigureAwait(false);
            if (answer is null)
            {
                return new UserRound(exchanges, questions.Skip(index).ToList(), false);
            }

            exchanges.Add(new Exchange(questions[index], answer.Trim()));
        }

        var allEmpty = exchanges.Count > 0 && exchanges.All(exchange => exchange.Answer.Length == 0);
        return new UserRound(exchanges, [], allEmpty);
    }

    private static string ShownQuestion(string advice, string question, int index)
    {
        if (index != 0 || string.IsNullOrWhiteSpace(advice))
        {
            return question;
        }

        return advice.Trim() + Environment.NewLine + Environment.NewLine + question;
    }

    private static string BuildFollowUpPrompt(AgentContext context, IReadOnlyList<Exchange> conversation, bool finalRound)
    {
        var builder = new StringBuilder();
        builder.Append("Auto-approve: ").Append(context.AutoApprove).AppendLine().AppendLine();
        builder.AppendLine(context.Prompt);
        if (conversation.Count > 0)
        {
            builder.AppendLine().AppendLine("Conversation so far:");
            foreach (var exchange in conversation)
            {
                builder.Append("User was asked \"").Append(exchange.Question).Append("\" and answered \"").Append(exchange.Answer).AppendLine("\".");
            }
        }

        if (finalRound)
        {
            builder.AppendLine().AppendLine("Final round: true");
        }

        return builder.ToString();
    }

    private static AgentResult Finish(
        AgentContext context,
        OrchestratorReply reply,
        IReadOnlyList<Exchange> conversation,
        IReadOnlyList<string> assumed,
        bool unanswered,
        TokenCount tokens)
    {
        var assumptions = new List<string>(reply.Assumptions);
        assumptions.AddRange(conversation.Select(exchange => $"User was asked \"{exchange.Question}\" and answered \"{exchange.Answer}\"."));
        assumptions.AddRange(assumed.Select(question => $"Assumed a default for: {question}"));
        if (reply.NeedsHuman && (context.AutoApprove || unanswered))
        {
            return new AgentResult(assumptions, reply.Requirements, Tokens: tokens, NeedsHuman: true, FailureMessage: "A decision needs a person.");
        }

        return new AgentResult(assumptions, reply.Requirements, Tokens: tokens);
    }

    private static OrchestratorReply? ReadReply(string text)
    {
        using var document = ChatText.ParseObject(text);
        if (document is null)
        {
            return null;
        }

        var root = document.RootElement;
        var requirements = root.TryGetProperty("requirements", out var body) ? body.GetRawText() : root.GetRawText();
        var advice = root.TryGetProperty("advice", out var adviceElement) && adviceElement.ValueKind == JsonValueKind.String
            ? adviceElement.GetString() ?? string.Empty
            : string.Empty;
        return new OrchestratorReply(ReadStrings(root, "assumptions"), ReadStrings(root, "questions"), advice, ReadNeedsHuman(root), requirements);
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

        return array.EnumerateArray().Select(ReadText).Where(item => !string.IsNullOrWhiteSpace(item)).Cast<string>().ToList();
    }

    private static string? ReadText(JsonElement item)
    {
        if (item.ValueKind == JsonValueKind.String)
        {
            return item.GetString();
        }

        if (item.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var name in TextPropertyNames)
        {
            if (item.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String)
            {
                return property.GetString();
            }
        }

        foreach (var property in item.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String)
            {
                return property.Value.GetString();
            }
        }

        return null;
    }

    private sealed record Exchange(string Question, string Answer);

    private sealed record OrchestratorReply(
        IReadOnlyList<string> Assumptions,
        IReadOnlyList<string> Questions,
        string Advice,
        bool NeedsHuman,
        string Requirements);

    private sealed record ModelTurn(OrchestratorReply? Reply, TokenCount Tokens);

    private sealed record UserRound(IReadOnlyList<Exchange> Exchanges, IReadOnlyList<string> Unanswered, bool AllEmpty);
}

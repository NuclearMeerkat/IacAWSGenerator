using IacAgent.Core;

namespace IacAgent.Plugins.Agents.Tests.Trio;

public sealed class OrchestratorAgentTests
{
    private const string Asking = """{"assumptions":["region is eu-central-1"],"questions":["Which region?"],"needsHuman":false,"requirements":{"summary":"offset"}}""";

    private const string Revised = """{"assumptions":["page numbers"],"questions":[],"advice":"","needsHuman":false,"requirements":{"summary":"page-number"}}""";

    [Fact]
    public async Task RunAsync_AutoApprove_MakesOneCallAndAsksNothing()
    {
        var interaction = new ScriptedInteraction("page number");
        var client = new ScriptedChatClient(Asking);
        var result = await RunAsync(client, interaction, autoApprove: true);

        Assert.Equal(1, client.Calls);
        Assert.Empty(interaction.Questions);
        Assert.Contains(result.Assumptions, assumption => assumption == "Assumed a default for: Which region?");
        Assert.Contains("offset", result.RequirementsJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_AutoApproveNeedsHuman_StopsForAPerson()
    {
        var interaction = new ScriptedInteraction();
        var client = new ScriptedChatClient("""{"assumptions":[],"questions":[],"needsHuman":true,"requirements":{"summary":"choose a database"}}""");
        var result = await RunAsync(client, interaction, autoApprove: true);

        Assert.Empty(interaction.Questions);
        Assert.True(result.NeedsHuman);
    }

    [Fact]
    public async Task RunAsync_UserAnswers_SecondCallContainsQuestionAndAnswer()
    {
        var interaction = new ScriptedInteraction("page number");
        var client = new ScriptedChatClient(Asking, Revised);
        var result = await RunAsync(client, interaction, autoApprove: false);

        Assert.Contains("Which region?", client.Users[1], StringComparison.Ordinal);
        Assert.Contains("page number", client.Users[1], StringComparison.Ordinal);
        Assert.Contains("page-number", result.RequirementsJson, StringComparison.Ordinal);
        Assert.Contains(result.Assumptions, assumption => assumption == "User was asked \"Which region?\" and answered \"page number\".");
    }

    [Fact]
    public async Task RunAsync_AllAnswersEmpty_MakesNoFollowUpCall()
    {
        var interaction = new ScriptedInteraction(string.Empty);
        var client = new ScriptedChatClient(Asking, Revised);
        var result = await RunAsync(client, interaction, autoApprove: false);

        Assert.Equal(1, client.Calls);
        Assert.Contains("offset", result.RequirementsJson, StringComparison.Ordinal);
        Assert.Contains(result.Assumptions, assumption => assumption == "User was asked \"Which region?\" and answered \"\".");
    }

    [Fact]
    public async Task RunAsync_ReplyWithAdvice_ShowsAdviceBeforeFollowUpQuestion()
    {
        const string advice = "Use page numbers because they stay stable.";
        var followUp = "{\"assumptions\":[],\"questions\":[\"Use page numbers? Press Enter to accept.\"],\"advice\":\"" + advice + "\",\"needsHuman\":false,\"requirements\":{\"summary\":\"page-number\"}}";
        var interaction = new ScriptedInteraction("What is better?", string.Empty);
        var client = new ScriptedChatClient(Asking, followUp);
        await RunAsync(client, interaction, autoApprove: false);

        var shown = interaction.Questions[1];
        var adviceAt = shown.IndexOf(advice, StringComparison.Ordinal);
        var questionAt = shown.IndexOf("Use page numbers?", StringComparison.Ordinal);
        Assert.True(adviceAt >= 0 && questionAt > adviceAt);
    }

    [Fact]
    public async Task RunAsync_ModelKeepsAsking_StopsAfterRoundLimit()
    {
        const string stillOpen = """{"assumptions":[],"questions":["Still open?"],"needsHuman":false,"requirements":{"summary":"default"}}""";
        var interaction = new ScriptedInteraction("more", "more", "more", "more");
        var client = new ScriptedChatClient(stillOpen, stillOpen, stillOpen, stillOpen);
        var result = await RunAsync(client, interaction, autoApprove: false);

        Assert.Equal(4, client.Calls);
        Assert.Equal(3, interaction.Questions.Count);
        Assert.Contains("Final round: true", client.Users[3], StringComparison.Ordinal);
        Assert.Contains(result.Assumptions, assumption => assumption == "Assumed a default for: Still open?");
    }

    [Fact]
    public async Task RunAsync_CannotPrompt_KeepsDefaultsAndMakesNoFollowUpCall()
    {
        var client = new ScriptedChatClient(Asking, Revised);
        var result = await RunAsync(client, new SilentInteraction(), autoApprove: false);

        Assert.Equal(1, client.Calls);
        Assert.False(result.NeedsHuman);
        Assert.Contains("offset", result.RequirementsJson, StringComparison.Ordinal);
        Assert.Contains(result.Assumptions, assumption => assumption == "Assumed a default for: Which region?");
    }

    [Fact]
    public async Task RunAsync_Dialogue_SumsTokensOfAllCalls()
    {
        var client = new ScriptedChatClient(Asking, Revised);
        var result = await RunAsync(client, new ScriptedInteraction("page number"), autoApprove: false);

        Assert.Equal(4, result.Tokens!.Input);
        Assert.Equal(6, result.Tokens.Output);
    }

    [Fact]
    public async Task RunAsync_AnswerContainsSecret_RequestIsRedacted()
    {
        const string secret = "super-secret-value";
        var client = new ScriptedChatClient(Asking, Revised);
        var rules = new IRedactionRule[] { new MaskRule(secret) };
        await RunAsync(client, new ScriptedInteraction(secret), autoApprove: false, rules);

        Assert.DoesNotContain(secret, client.Users[1], StringComparison.Ordinal);
        Assert.Contains("[redacted]", client.Users[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_InvalidFollowUpJson_FailsAfterOneFormatRetry()
    {
        var client = new ScriptedChatClient(Asking, "not-json", "still-not");
        var result = await RunAsync(client, new ScriptedInteraction("page number"), autoApprove: false);

        Assert.Equal(3, client.Calls);
        Assert.True(result.Failed);
        Assert.Equal("The orchestrator did not return JSON.", result.FailureMessage);
    }

    private static async Task<AgentResult> RunAsync(
        ScriptedChatClient client,
        IUserInteraction interaction,
        bool autoApprove,
        IEnumerable<IRedactionRule>? rules = null)
    {
        using var scope = AgentTestHost.Open(client, interaction, rules: rules);
        var orchestrator = scope.Profile("trio").Agents.Single(agent => agent.Role == AgentRole.Orchestrator);
        return await orchestrator.RunAsync(new AgentContext("api", autoApprove), CancellationToken.None);
    }

    private sealed class ScriptedInteraction(params string?[] answers) : IUserInteraction
    {
        private readonly Queue<string?> _answers = new(answers);

        public List<string> Questions { get; } = [];

        public Task<string?> AskAsync(string question, CancellationToken cancellationToken)
        {
            Questions.Add(question);
            return Task.FromResult(_answers.Count == 0 ? string.Empty : _answers.Dequeue());
        }
    }

    private sealed class MaskRule(string secret) : IRedactionRule
    {
        public string Redact(string text)
        {
            return text.Replace(secret, "[redacted]", StringComparison.Ordinal);
        }
    }
}

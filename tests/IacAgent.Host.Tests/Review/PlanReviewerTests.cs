using IacAgent.Core;
using IacAgent.Host;
using IacAgent.Host.Redaction;
using IacAgent.Host.Review;
using IacAgent.Plugins.Gates;
using IacAgent.Plugins.Gates.Infracost;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IacAgent.Host.Tests.Review;

public sealed class PlanReviewerTests
{
    private const string SecretPassword = "supersecretvalue";
    private const string SecretKey = "AKIAIOSFODNN7EXAMPLE";

    [Fact]
    public async Task ReviewAsync_SeededIssue_ReportsFindingWithoutSecrets()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "fixtures", "insecure");
        var client = new EchoChatClient();
        var services = new ServiceCollection();
        services.AddSingleton<IProcessRunner>(new CheckovProcessRunner());
        services.AddSingleton(new BudgetLimit(null));
        services.AddSingleton<IRedactionRule, AwsAccessKeyRedactionRule>();
        services.AddSingleton<IRedactionRule, JsonSecretRedactionRule>();
        services.AddSingleton<IModelRouter>(new FixedRouter(client));
        services.AddGates(new ConfigurationBuilder().Build());
        services.AddHost(new ConfigurationBuilder().Build());
        var reviewer = services.BuildServiceProvider().GetRequiredService<PlanReviewer>();

        var review = await reviewer.ReviewAsync(Path.Combine(directory, "plan.json"), directory, CancellationToken.None);

        Assert.Contains(review.Findings, finding => finding.RuleId == "CKV_AWS_24");
        Assert.DoesNotContain(SecretPassword, client.LastUser, StringComparison.Ordinal);
        Assert.DoesNotContain(SecretKey, client.LastUser, StringComparison.Ordinal);
        Assert.DoesNotContain(SecretPassword, review.Explanation, StringComparison.Ordinal);
        Assert.DoesNotContain(SecretKey, review.Explanation, StringComparison.Ordinal);
        Assert.Contains("[redacted]", review.Explanation, StringComparison.Ordinal);
    }

    private sealed class FixedRouter(IChatClient client) : IModelRouter
    {
        public IChatClient GetClient(ModelTier tier)
        {
            return client;
        }
    }

    private sealed class EchoChatClient : IChatClient
    {
        public string LastUser { get; private set; } = string.Empty;

        public void Dispose()
        {
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
        {
            return null;
        }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            LastUser = messages.Last().Text ?? string.Empty;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, LastUser)));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class CheckovProcessRunner : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory, CancellationToken cancellationToken)
        {
            const string json = """
                {"results":{"failed_checks":[{"check_id":"CKV_AWS_24","check_name":"Ensure no security groups allow ingress from 0.0.0.0/0 to port 22","file_path":"/main.tf","file_line_range":[5,12]}]}}
                """;
            return Task.FromResult(new ProcessResult(1, json, string.Empty));
        }
    }
}

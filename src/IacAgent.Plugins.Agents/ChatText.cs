using System.Text.Json;

using IacAgent.Core;

using Microsoft.Extensions.AI;

namespace IacAgent.Plugins.Agents;

internal static class ChatText
{
    public static async Task<(string Text, TokenCount Tokens)> CompleteAsync(
        IChatClient client,
        string systemPrompt,
        string userPrompt,
        IEnumerable<IRedactionRule> rules,
        ChatOptions? options,
        CancellationToken cancellationToken)
    {
        var redacted = userPrompt;
        foreach (var rule in rules)
        {
            redacted = rule.Redact(redacted);
        }

        var response = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.System, systemPrompt), new ChatMessage(ChatRole.User, redacted)],
            options,
            cancellationToken).ConfigureAwait(false);
        var input = (int)(response.Usage?.InputTokenCount ?? 0);
        var output = (int)(response.Usage?.OutputTokenCount ?? 0);
        return (response.Text ?? string.Empty, new TokenCount(input, output, ReadCost(response.Usage)));
    }

    private static decimal? ReadCost(UsageDetails? usage)
    {
        if (usage?.AdditionalCounts is not { } counts)
        {
            return null;
        }

        foreach (var pair in counts)
        {
            if (pair.Key.Equals("cost", StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return null;
    }

    public static string ReadPrompt(string fileName)
    {
        var name = $"IacAgent.Plugins.Agents.Prompts.{fileName}";
        using var stream = typeof(ChatText).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Prompt '{fileName}' is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static string StripFence(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var firstNewLine = trimmed.IndexOf('\n');
        var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        if (firstNewLine < 0 || lastFence <= firstNewLine)
        {
            return trimmed;
        }

        return trimmed[(firstNewLine + 1)..lastFence].Trim();
    }

    public static JsonDocument? ParseObject(string text)
    {
        try
        {
            var document = JsonDocument.Parse(StripFence(text));
            return document.RootElement.ValueKind == JsonValueKind.Object ? document : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

using System.Text;
using System.Text.Json;

using IacAgent.Core;

using Microsoft.Extensions.AI;

namespace IacAgent.Plugins.Agents;

internal static class TerraformReply
{
    public static string UserMessage(AgentContext context)
    {
        var builder = new StringBuilder();
        builder.AppendLine(context.RequirementsJson ?? context.Prompt);
        if (context.GateResults is null)
        {
            return builder.ToString();
        }

        builder.AppendLine();
        builder.AppendLine("Fix the cited resources. Do not add a VPC, NAT gateway, KMS key, or code-signing resource unless an issue names it.");
        builder.AppendLine("Gate issues:");
        foreach (var result in context.GateResults.Where(result => !result.Passed))
        {
            foreach (var issue in result.Issues)
            {
                builder.Append(result.GateName).Append(' ').Append(issue.File).Append(':').Append(issue.Line).Append(' ');
                builder.Append('[').Append(issue.Severity).Append("] ").Append(issue.RuleId).Append(": ").AppendLine(issue.Message);
            }
        }

        if (context.Artifact is not null)
        {
            foreach (var file in context.Artifact.Files)
            {
                builder.AppendLine();
                builder.AppendLine($"File {file.RelativePath}:");
                builder.AppendLine(file.Content);
            }
        }

        return builder.ToString();
    }

    public static async Task<AgentResult> WriteAsync(
        IChatClient client,
        string systemPrompt,
        string userPrompt,
        IEnumerable<IRedactionRule> rules,
        CancellationToken cancellationToken)
    {
        var (text, tokens) = await ChatText.CompleteAsync(client, systemPrompt, userPrompt, rules, cancellationToken).ConfigureAwait(false);
        if (TryRead(text, out var files, out var assumptions))
        {
            return new AgentResult(assumptions, Files: files, Tokens: tokens);
        }

        var (repaired, retryTokens) = await ChatText.CompleteAsync(
            client,
            systemPrompt,
            "Reply with only the JSON object {\"files\":[{\"path\":\"main.tf\",\"content\":\"...\"}]}.\n\n" + text,
            rules,
            cancellationToken).ConfigureAwait(false);
        tokens = new TokenCount(tokens.Input + retryTokens.Input, tokens.Output + retryTokens.Output);
        if (TryRead(repaired, out files, out assumptions))
        {
            return new AgentResult(assumptions, Files: files, Tokens: tokens);
        }

        return new AgentResult([], Tokens: tokens, Failed: true, FailureMessage: "The developer did not return a files JSON object.");
    }

    private static bool TryRead(string text, out IReadOnlyList<GeneratedFile> files, out IReadOnlyList<string> assumptions)
    {
        files = [];
        assumptions = [];
        var document = ChatText.ParseObject(text);
        if (document is null)
        {
            return false;
        }

        using (document)
        {
            assumptions = ReadAssumptions(document.RootElement);
            if (!document.RootElement.TryGetProperty("files", out var fileArray) || fileArray.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var parsed = new List<GeneratedFile>();
            foreach (var file in fileArray.EnumerateArray())
            {
                var path = file.TryGetProperty("path", out var pathElement) ? pathElement.GetString() : null;
                var content = file.TryGetProperty("content", out var contentElement) ? contentElement.GetString() : null;
                if (string.IsNullOrWhiteSpace(path) || content is null)
                {
                    return false;
                }

                parsed.Add(new GeneratedFile(path, content));
            }

            files = parsed;
            return parsed.Count > 0;
        }
    }

    private static IReadOnlyList<string> ReadAssumptions(JsonElement element)
    {
        if (!element.TryGetProperty("assumptions", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return array.EnumerateArray().Select(item => item.GetString()).Where(item => !string.IsNullOrWhiteSpace(item)).Cast<string>().ToList();
    }
}

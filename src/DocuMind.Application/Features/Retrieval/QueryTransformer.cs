using System.Text;
using DocuMind.Domain.Conversations;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace DocuMind.Application.Features.Retrieval;

public interface IQueryTransformer
{
    /// <summary>Turns a follow-up ("what about the second one?") into a standalone search query using chat history.</summary>
    Task<string> RewriteAsync(string question, IReadOnlyList<Message> history, CancellationToken ct);

    /// <summary>Generates alternative phrasings to improve recall (multi-query retrieval).</summary>
    Task<IReadOnlyList<string>> ExpandAsync(string query, CancellationToken ct);
}

public sealed class QueryTransformer(IChatClient chatClient, ILogger<QueryTransformer> logger) : IQueryTransformer
{
    public async Task<string> RewriteAsync(string question, IReadOnlyList<Message> history, CancellationToken ct)
    {
        if (history.Count == 0)
            return question;

        var transcript = new StringBuilder();
        foreach (var message in history.TakeLast(6))
            transcript.AppendLine($"{message.Role}: {Truncate(message.Content, 500)}");

        var prompt = $"""
            Rewrite the final user question into a single standalone search query that can be understood without
            the conversation. Resolve pronouns and references using the conversation. Keep the original language.
            Output ONLY the rewritten query, no explanation, no quotes.

            Conversation:
            {transcript}
            Final question: {question}
            """;

        try
        {
            var response = await chatClient.GetResponseAsync(prompt, new ChatOptions { Temperature = 0 }, ct);
            var rewritten = response.Text.Trim().Trim('"', '\'').Trim();
            return string.IsNullOrWhiteSpace(rewritten) || rewritten.Length > 500 ? question : rewritten;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Query rewriting failed, using original question");
            return question;
        }
    }

    public async Task<IReadOnlyList<string>> ExpandAsync(string query, CancellationToken ct)
    {
        var prompt = $"""
            Generate 2 alternative search queries for the question below that use different wording or synonyms,
            to help retrieve relevant documents. Keep the original language.
            Question: {query}
            """;

        try
        {
            var response = await chatClient.GetResponseAsync<ExpansionResult>(prompt, new ChatOptions { Temperature = 0.3f }, cancellationToken: ct);
            return response.TryGetResult(out var result) && result.Queries is { Length: > 0 }
                ? result.Queries.Where(q => !string.IsNullOrWhiteSpace(q)).Take(2).ToList()
                : [];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Query expansion failed");
            return [];
        }
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max] + "...";

    private sealed record ExpansionResult(string[] Queries);
}

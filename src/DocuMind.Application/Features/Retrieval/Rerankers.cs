using System.Text;
using DocuMind.Application.Common;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace DocuMind.Application.Features.Retrieval;

public interface IReranker
{
    Task<IReadOnlyList<RetrievedChunk>> RerankAsync(string query, IReadOnlyList<RetrievedChunk> candidates, int topK,
        CancellationToken ct);
}

/// <summary>
/// LLM-as-a-reranker (pointwise listwise hybrid): asks the chat model to score every candidate 0-10 in one call.
/// A cross-encoder (e.g. bge-reranker) is faster in production; this keeps the stack dependency free.
/// </summary>
public sealed class LlmReranker(IChatClient chatClient, ILogger<LlmReranker> logger) : IReranker
{
    public async Task<IReadOnlyList<RetrievedChunk>> RerankAsync(string query, IReadOnlyList<RetrievedChunk> candidates,
        int topK, CancellationToken ct)
    {
        if (candidates.Count <= 1)
            return candidates;

        using var activity = DocuMindTelemetry.ActivitySource.StartActivity("retrieval.rerank");
        activity?.SetTag("documind.candidates", candidates.Count);

        var passages = new StringBuilder();
        for (var i = 0; i < candidates.Count; i++)
        {
            var content = candidates[i].Content;
            passages.AppendLine($"[{i + 1}] {(content.Length > 700 ? content[..700] : content)}").AppendLine();
        }

        var prompt = $"""
            You are a relevance judge for a search engine. Score how well each passage helps answer the query,
            from 0 (irrelevant) to 10 (directly answers it). Return a score for EVERY passage id.

            Query: {query}

            Passages:
            {passages}
            """;

        try
        {
            var response = await chatClient.GetResponseAsync<RerankResult>(prompt, new ChatOptions { Temperature = 0 }, cancellationToken: ct);
            if (!response.TryGetResult(out var result) || result.Scores is not { Length: > 0 })
                return candidates.Take(topK).ToList();

            var scores = result.Scores
                .Where(s => s.Id >= 1 && s.Id <= candidates.Count)
                .GroupBy(s => s.Id)
                .ToDictionary(g => g.Key - 1, g => g.First().Score);

            return candidates
                .Select((chunk, index) => (chunk, index, score: scores.GetValueOrDefault(index, -1)))
                .OrderByDescending(x => x.score)
                .ThenBy(x => x.index) // stable: keep retrieval order on ties
                .Take(topK)
                .Select(x => x.chunk with { Score = x.score >= 0 ? x.score / 10.0 : x.chunk.Score })
                .ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Reranking failed, falling back to retrieval order");
            return candidates.Take(topK).ToList();
        }
    }

    private sealed record RerankResult(PassageScore[] Scores);

    private sealed record PassageScore(int Id, double Score);
}

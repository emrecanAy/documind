using System.Diagnostics;
using DocuMind.Application.Abstractions;
using DocuMind.Application.Common;

namespace DocuMind.Application.Features.Retrieval;

public interface IRetriever
{
    Task<RetrievalResult> RetrieveAsync(RetrievalRequest request, CancellationToken ct);
}

/// <summary>
/// Retrieval pipeline: (optional) query expansion → hybrid dense + BM25 search fused with RRF inside Qdrant →
/// cross-query RRF → (optional) LLM reranking down to Top-K.
/// </summary>
public sealed class HybridRetriever(
    IEmbeddingService embeddings,
    IVectorStore vectorStore,
    IQueryTransformer queryTransformer,
    IReranker reranker) : IRetriever
{
    private const int CandidateMultiplier = 3;

    public async Task<RetrievalResult> RetrieveAsync(RetrievalRequest request, CancellationToken ct)
    {
        using var activity = DocuMindTelemetry.ActivitySource.StartActivity("retrieval");
        activity?.SetTag("documind.workspace_id", request.WorkspaceId);
        activity?.SetTag("documind.hybrid", request.Settings.UseHybridSearch);
        activity?.SetTag("documind.rerank", request.Settings.UseReranker);
        var stopwatch = Stopwatch.StartNew();

        var settings = request.Settings;
        var queries = new List<string> { request.Query };
        if (settings.UseQueryExpansion)
            queries.AddRange(await queryTransformer.ExpandAsync(request.Query, ct));

        var candidateLimit = settings.UseReranker ? settings.TopK * CandidateMultiplier : settings.TopK;

        var rankings = await Task.WhenAll(queries.Select(q => SearchAsync(q, request, candidateLimit, ct)));
        var candidates = rankings.Length == 1
            ? rankings[0]
            : ReciprocalRankFusion.Fuse(rankings).Take(candidateLimit).ToList();

        var results = settings.UseReranker
            ? await reranker.RerankAsync(request.Query, candidates, settings.TopK, ct)
            : candidates.Take(settings.TopK).ToList();

        stopwatch.Stop();
        DocuMindTelemetry.RetrievalDuration.Record(stopwatch.Elapsed.TotalMilliseconds);
        activity?.SetTag("documind.results", results.Count);
        return new RetrievalResult(results, queries, stopwatch.ElapsedMilliseconds);
    }

    private async Task<IReadOnlyList<RetrievedChunk>> SearchAsync(string query, RetrievalRequest request, int limit,
        CancellationToken ct)
    {
        var dense = await embeddings.EmbedQueryAsync(query, ct);
        var sparse = request.Settings.UseHybridSearch ? Bm25SparseEncoder.EncodeQuery(query) : null;
        if (sparse is { Indices.Length: 0 })
            sparse = null;

        var hits = await vectorStore.SearchAsync(
            new VectorQuery(request.WorkspaceId, dense, sparse, limit, request.DocumentId), ct);

        return hits
            .Select(h => new RetrievedChunk(h.Id, h.DocumentId, h.FileName, h.ChunkIndex, h.Page, h.Section, h.Content, h.Score))
            .ToList();
    }
}

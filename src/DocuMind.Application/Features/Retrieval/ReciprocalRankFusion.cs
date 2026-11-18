namespace DocuMind.Application.Features.Retrieval;

/// <summary>
/// RRF: score(d) = Σ 1 / (k + rank_i(d)). Rank-based, so it merges result lists whose raw scores are not comparable
/// (e.g. results from several query reformulations).
/// </summary>
public static class ReciprocalRankFusion
{
    public static IReadOnlyList<RetrievedChunk> Fuse(IEnumerable<IReadOnlyList<RetrievedChunk>> rankings, int k = 60)
    {
        var scores = new Dictionary<Guid, (RetrievedChunk Chunk, double Score)>();
        foreach (var ranking in rankings)
        {
            for (var rank = 0; rank < ranking.Count; rank++)
            {
                var chunk = ranking[rank];
                var contribution = 1.0 / (k + rank + 1);
                scores[chunk.ChunkId] = scores.TryGetValue(chunk.ChunkId, out var existing)
                    ? (existing.Chunk, existing.Score + contribution)
                    : (chunk, contribution);
            }
        }

        return scores.Values
            .OrderByDescending(v => v.Score)
            .Select(v => v.Chunk with { Score = v.Score })
            .ToList();
    }
}

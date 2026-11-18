using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using DocuMind.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using static Qdrant.Client.Grpc.Conditions;
using SparseVector = DocuMind.Application.Abstractions.SparseVector;

namespace DocuMind.Infrastructure.VectorStore;

/// <summary>
/// Qdrant implementation using named vectors: "dense" (cosine) + "sparse" (BM25 with server-side IDF).
/// Hybrid queries prefetch both and fuse them with Reciprocal Rank Fusion inside Qdrant (Query API).
/// One collection per embedding model + dimension, so switching models never mixes incompatible vectors.
/// Tenancy: every point carries a keyword-indexed workspace_id and every query/delete is filtered by it.
/// </summary>
internal sealed partial class QdrantVectorStore(
    QdrantClient client,
    IEmbeddingService embeddings,
    ILogger<QdrantVectorStore> logger) : IVectorStore
{
    private const string Dense = "dense";
    private const string Sparse = "sparse";
    private static readonly ConcurrentDictionary<string, bool> EnsuredCollections = new();
    private static readonly SemaphoreSlim CreateLock = new(1, 1);

    public async Task EnsureCollectionAsync(int dimensions, CancellationToken ct)
    {
        var name = CollectionName(dimensions);
        if (EnsuredCollections.ContainsKey(name))
            return;

        await CreateLock.WaitAsync(ct); // serialize concurrent consumers inside this process
        try
        {
            await CreateIfMissingAsync(name, dimensions, ct);
        }
        finally
        {
            CreateLock.Release();
        }
    }

    private async Task CreateIfMissingAsync(string name, int dimensions, CancellationToken ct)
    {
        if (EnsuredCollections.ContainsKey(name))
            return;

        if (!await client.CollectionExistsAsync(name, ct))
        {
            logger.LogInformation("Creating Qdrant collection {Collection} ({Dimensions} dims)", name, dimensions);
            try
            {
                await client.CreateCollectionAsync(
                    name,
                    new VectorParamsMap { Map = { [Dense] = new VectorParams { Size = (ulong)dimensions, Distance = Distance.Cosine } } },
                    sparseVectorsConfig: new SparseVectorConfig { Map = { [Sparse] = new SparseVectorParams { Modifier = Modifier.Idf } } },
                    cancellationToken: ct);
                await client.CreatePayloadIndexAsync(name, "workspace_id", PayloadSchemaType.Keyword,
                    indexParams: new PayloadIndexParams { KeywordIndexParams = new KeywordIndexParams { IsTenant = true } },
                    cancellationToken: ct);
                await client.CreatePayloadIndexAsync(name, "document_id", PayloadSchemaType.Keyword, cancellationToken: ct);
            }
            catch (Grpc.Core.RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.AlreadyExists)
            {
                // another instance created it concurrently
            }
        }

        EnsuredCollections[name] = true;
    }

    public async Task UpsertAsync(IReadOnlyList<VectorRecord> records, CancellationToken ct)
    {
        if (records.Count == 0)
            return;

        var name = CollectionName(records[0].Dense.Length);
        var points = records.Select(r =>
        {
            var point = new PointStruct
            {
                Id = r.Id,
                Vectors = new Dictionary<string, Vector>
                {
                    [Dense] = r.Dense,
                    [Sparse] = (r.Sparse.Values, r.Sparse.Indices),
                },
                Payload =
                {
                    ["workspace_id"] = r.WorkspaceId.ToString(),
                    ["document_id"] = r.DocumentId.ToString(),
                    ["file_name"] = r.FileName,
                    ["chunk_index"] = r.ChunkIndex,
                    ["content"] = r.Content,
                },
            };
            if (r.Page is not null)
                point.Payload["page"] = r.Page.Value;
            if (r.Section is not null)
                point.Payload["section"] = r.Section;
            return point;
        }).ToList();

        await client.UpsertAsync(name, points, wait: true, cancellationToken: ct);
    }

    public async Task<IReadOnlyList<VectorHit>> SearchAsync(VectorQuery query, CancellationToken ct)
    {
        var name = CollectionName(query.Dense.Length);
        if (!await CollectionExistsAsync(name, ct))
            return [];

        var filter = TenantFilter(query.WorkspaceId, query.DocumentId);
        IReadOnlyList<ScoredPoint> points;

        if (query.Sparse is { } sparse)
        {
            var candidates = (ulong)Math.Max(query.Limit * 2, 20);
            points = await client.QueryAsync(
                name,
                query: Fusion.Rrf,
                prefetch:
                [
                    new PrefetchQuery { Query = query.Dense, Using = Dense, Filter = filter, Limit = candidates },
                    new PrefetchQuery { Query = (sparse.Values, sparse.Indices), Using = Sparse, Filter = filter, Limit = candidates },
                ],
                filter: filter,
                limit: (ulong)query.Limit,
                payloadSelector: true,
                cancellationToken: ct);
        }
        else
        {
            points = await client.QueryAsync(
                name,
                query: query.Dense,
                usingVector: Dense,
                filter: filter,
                limit: (ulong)query.Limit,
                payloadSelector: true,
                cancellationToken: ct);
        }

        return points.Select(ToHit).ToList();
    }

    public async Task DeleteDocumentAsync(Guid workspaceId, Guid documentId, CancellationToken ct)
    {
        var name = CollectionName(await embeddings.GetDimensionsAsync(ct));
        if (await CollectionExistsAsync(name, ct))
            await client.DeleteAsync(name, TenantFilter(workspaceId, documentId), wait: true, cancellationToken: ct);
    }

    public async Task DeleteWorkspaceAsync(Guid workspaceId, CancellationToken ct)
    {
        var name = CollectionName(await embeddings.GetDimensionsAsync(ct));
        if (await CollectionExistsAsync(name, ct))
            await client.DeleteAsync(name, TenantFilter(workspaceId, null), wait: true, cancellationToken: ct);
    }

    private static Filter TenantFilter(Guid workspaceId, Guid? documentId)
    {
        var filter = new Filter { Must = { MatchKeyword("workspace_id", workspaceId.ToString()) } };
        if (documentId is not null)
            filter.Must.Add(MatchKeyword("document_id", documentId.Value.ToString()));
        return filter;
    }

    private async Task<bool> CollectionExistsAsync(string name, CancellationToken ct) =>
        EnsuredCollections.ContainsKey(name) || await client.CollectionExistsAsync(name, ct);

    private string CollectionName(int dimensions) =>
        $"documind_{InvalidChars().Replace(embeddings.ModelId.ToLowerInvariant(), "_")}_{dimensions}";

    private static VectorHit ToHit(ScoredPoint point)
    {
        var payload = point.Payload;
        return new VectorHit(
            Guid.Parse(point.Id.Uuid),
            Guid.Parse(payload["document_id"].StringValue),
            payload["file_name"].StringValue,
            (int)payload["chunk_index"].IntegerValue,
            payload.TryGetValue("page", out var page) ? (int)page.IntegerValue : null,
            payload.TryGetValue("section", out var section) ? section.StringValue : null,
            payload["content"].StringValue,
            point.Score);
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex InvalidChars();
}

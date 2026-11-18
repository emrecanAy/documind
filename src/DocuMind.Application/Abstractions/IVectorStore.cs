namespace DocuMind.Application.Abstractions;

public sealed record SparseVector(uint[] Indices, float[] Values);

public sealed record VectorRecord(
    Guid Id,
    Guid WorkspaceId,
    Guid DocumentId,
    string FileName,
    int ChunkIndex,
    int? Page,
    string? Section,
    string Content,
    float[] Dense,
    SparseVector Sparse);

public sealed record VectorQuery(
    Guid WorkspaceId,
    float[] Dense,
    SparseVector? Sparse,
    int Limit,
    Guid? DocumentId = null);

public sealed record VectorHit(
    Guid Id,
    Guid DocumentId,
    string FileName,
    int ChunkIndex,
    int? Page,
    string? Section,
    string Content,
    double Score);

/// <summary>
/// Vector database abstraction. Implementations MUST always filter by <see cref="VectorQuery.WorkspaceId"/>
/// so tenants can never read each other's data.
/// </summary>
public interface IVectorStore
{
    Task EnsureCollectionAsync(int dimensions, CancellationToken ct);
    Task UpsertAsync(IReadOnlyList<VectorRecord> records, CancellationToken ct);
    Task<IReadOnlyList<VectorHit>> SearchAsync(VectorQuery query, CancellationToken ct);
    Task DeleteDocumentAsync(Guid workspaceId, Guid documentId, CancellationToken ct);
    Task DeleteWorkspaceAsync(Guid workspaceId, CancellationToken ct);
}

/// <summary>Embedding generation with model specific prefixes and batching.</summary>
public interface IEmbeddingService
{
    string ModelId { get; }
    Task<int> GetDimensionsAsync(CancellationToken ct);
    Task<float[]> EmbedQueryAsync(string text, CancellationToken ct);
    Task<IReadOnlyList<float[]>> EmbedDocumentsAsync(IReadOnlyList<string> texts, CancellationToken ct);
}

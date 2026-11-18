using DocuMind.Domain.Workspaces;

namespace DocuMind.Application.Features.Retrieval;

public sealed record RetrievalRequest(Guid WorkspaceId, string Query, RagSettings Settings, Guid? DocumentId = null);

public sealed record RetrievedChunk(
    Guid ChunkId,
    Guid DocumentId,
    string FileName,
    int ChunkIndex,
    int? Page,
    string? Section,
    string Content,
    double Score);

public sealed record RetrievalResult(IReadOnlyList<RetrievedChunk> Chunks, IReadOnlyList<string> Queries, long ElapsedMs);

using DocuMind.Domain.Common;

namespace DocuMind.Domain.Documents;

public enum DocumentStatus
{
    Queued,
    Processing,
    Indexed,
    Failed,
}

public sealed class Document : Entity
{
    private Document() { }

    public Guid WorkspaceId { get; private init; }
    public string FileName { get; private init; } = string.Empty;
    public string ContentType { get; private init; } = string.Empty;
    public long SizeBytes { get; private init; }
    public string StoragePath { get; private init; } = string.Empty;
    public string Sha256 { get; private init; } = string.Empty;
    public Guid UploadedBy { get; private init; }
    public DocumentStatus Status { get; private set; }
    public int Progress { get; private set; }
    public string? Error { get; private set; }
    public int ChunkCount { get; private set; }
    public int PageCount { get; private set; }
    public string? EmbeddingModel { get; private set; }
    public DateTimeOffset? IndexedAt { get; private set; }

    public static Document Create(Guid workspaceId, string fileName, string contentType, long sizeBytes,
        string storagePath, string sha256, Guid uploadedBy) => new()
    {
        WorkspaceId = workspaceId,
        FileName = fileName,
        ContentType = contentType,
        SizeBytes = sizeBytes,
        StoragePath = storagePath,
        Sha256 = sha256,
        UploadedBy = uploadedBy,
        Status = DocumentStatus.Queued,
    };

    public void MarkQueued()
    {
        Status = DocumentStatus.Queued;
        Progress = 0;
        Error = null;
    }

    public void MarkProcessing(int progress)
    {
        Status = DocumentStatus.Processing;
        Progress = Math.Clamp(progress, 0, 100);
    }

    public void MarkIndexed(int chunkCount, int pageCount, string embeddingModel)
    {
        Status = DocumentStatus.Indexed;
        Progress = 100;
        ChunkCount = chunkCount;
        PageCount = pageCount;
        EmbeddingModel = embeddingModel;
        IndexedAt = DateTimeOffset.UtcNow;
        Error = null;
    }

    public void MarkFailed(string error, bool willRetry)
    {
        Status = willRetry ? DocumentStatus.Queued : DocumentStatus.Failed;
        Progress = 0;
        Error = error.Length > 2000 ? error[..2000] : error;
    }
}

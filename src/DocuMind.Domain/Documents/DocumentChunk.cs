namespace DocuMind.Domain.Documents;

/// <summary>
/// Relational copy of an indexed chunk. The vector lives in Qdrant under the same Id;
/// keeping the text here lets us render citations and expand sections without the vector store.
/// </summary>
public sealed class DocumentChunk
{
    private DocumentChunk() { }

    public DocumentChunk(Guid id, Guid documentId, Guid workspaceId, int index, string content,
        int? page, string? section, int tokenCount)
    {
        Id = id;
        DocumentId = documentId;
        WorkspaceId = workspaceId;
        Index = index;
        Content = content;
        Page = page;
        Section = section;
        TokenCount = tokenCount;
    }

    public Guid Id { get; private init; }
    public Guid DocumentId { get; private init; }
    public Guid WorkspaceId { get; private init; }
    public int Index { get; private init; }
    public string Content { get; private init; } = string.Empty;
    public int? Page { get; private init; }
    public string? Section { get; private init; }
    public int TokenCount { get; private init; }
}

using System.ComponentModel;
using System.Text;
using DocuMind.Application.Abstractions;
using DocuMind.Application.Features.Retrieval;
using DocuMind.Domain.Conversations;
using DocuMind.Domain.Documents;
using DocuMind.Domain.Workspaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace DocuMind.Application.Features.Chat;

/// <summary>
/// Tools exposed to the LLM in agent mode. Every tool is bound to a single workspace at construction time,
/// so the model can never reach other tenants' data regardless of the arguments it produces.
/// Passages get stable citation numbers across tool calls so the final answer can cite them.
/// </summary>
public sealed class AgentToolset(IAppDbContext db, IRetriever retriever, Guid workspaceId, RagSettings settings)
{
    private readonly Dictionary<Guid, Citation> _citations = [];

    public IReadOnlyList<Citation> Citations => _citations.Values.OrderBy(c => c.Number).ToList();

    public IList<AITool> CreateTools() =>
    [
        AIFunctionFactory.Create(SearchDocumentsAsync, "search_documents",
            "Semantic + keyword search over the workspace documents. Returns numbered passages with file name and page."),
        AIFunctionFactory.Create(ListDocumentsAsync, "list_documents",
            "Lists the indexed documents in the workspace with their ids and page counts."),
        AIFunctionFactory.Create(GetDocumentSectionAsync, "get_document_section",
            "Reads a passage together with its neighbouring chunks to get more surrounding context."),
    ];

    private async Task<string> SearchDocumentsAsync(
        [Description("A focused search query.")] string query,
        [Description("Optional document id to restrict the search to a single document.")] string? documentId = null,
        CancellationToken ct = default)
    {
        Guid? docId = Guid.TryParse(documentId, out var parsed) ? parsed : null;
        var result = await retriever.RetrieveAsync(new RetrievalRequest(workspaceId, query, settings, docId), ct);
        if (result.Chunks.Count == 0)
            return "No relevant passages found.";

        var output = new StringBuilder();
        foreach (var chunk in result.Chunks)
            output.Append(ContextBuilder.Format(Register(chunk), chunk)).AppendLine($"(document_id: {chunk.DocumentId}, chunk_index: {chunk.ChunkIndex})").AppendLine();
        return output.ToString();
    }

    private async Task<string> ListDocumentsAsync(CancellationToken ct = default)
    {
        var documents = await db.Documents
            .Where(d => d.WorkspaceId == workspaceId && d.Status == DocumentStatus.Indexed)
            .OrderBy(d => d.FileName)
            .Select(d => new { d.Id, d.FileName, d.PageCount, d.ChunkCount })
            .ToListAsync(ct);

        return documents.Count == 0
            ? "The workspace has no indexed documents."
            : string.Join('\n', documents.Select(d => $"- {d.FileName} (document_id: {d.Id}, pages: {d.PageCount}, chunks: {d.ChunkCount})"));
    }

    private async Task<string> GetDocumentSectionAsync(
        [Description("The document id returned by search_documents or list_documents.")] string documentId,
        [Description("The chunk index to center on.")] int chunkIndex,
        CancellationToken ct = default)
    {
        if (!Guid.TryParse(documentId, out var docId))
            return "Invalid document id.";

        var rows = await db.DocumentChunks
            .Where(c => c.WorkspaceId == workspaceId && c.DocumentId == docId && c.Index >= chunkIndex - 1 && c.Index <= chunkIndex + 1)
            .OrderBy(c => c.Index)
            .Join(db.Documents, c => c.DocumentId, d => d.Id, (c, d) => new { Chunk = c, d.FileName })
            .ToListAsync(ct);

        if (rows.Count == 0)
            return "Section not found.";

        var output = new StringBuilder();
        foreach (var row in rows)
        {
            var chunk = new RetrievedChunk(row.Chunk.Id, row.Chunk.DocumentId, row.FileName, row.Chunk.Index, row.Chunk.Page,
                row.Chunk.Section, row.Chunk.Content, 1);
            output.Append(ContextBuilder.Format(Register(chunk), chunk));
        }

        return output.ToString();
    }

    private int Register(RetrievedChunk chunk)
    {
        if (_citations.TryGetValue(chunk.ChunkId, out var existing))
            return existing.Number;
        var citation = ContextBuilder.ToCitation(_citations.Count + 1, chunk);
        _citations[chunk.ChunkId] = citation;
        return citation.Number;
    }
}

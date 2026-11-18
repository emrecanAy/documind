using System.Diagnostics;
using DocuMind.Application.Abstractions;
using DocuMind.Application.Common;
using DocuMind.Application.Features.Ingestion.Chunking;
using DocuMind.Application.Features.Retrieval;
using DocuMind.Domain.Documents;
using DocuMind.Domain.Ingestion;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DocuMind.Application.Features.Ingestion;

/// <summary>
/// parse -> chunk -> embed (dense + sparse) -> upsert. Idempotent: re-processing a document replaces its chunks.
/// </summary>
public sealed class IngestionPipeline(
    IAppDbContext db,
    IFileStorage storage,
    IEnumerable<IDocumentParser> parsers,
    IChunkerFactory chunkers,
    IEmbeddingService embeddings,
    IVectorStore vectorStore,
    IDocumentStatusNotifier notifier,
    ILogger<IngestionPipeline> logger)
{
    public static readonly IReadOnlySet<string> SupportedExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".pdf", ".docx", ".md", ".markdown", ".txt", ".html", ".htm" };

    public async Task ProcessAsync(IngestionJob job, CancellationToken ct)
    {
        using var activity = DocuMindTelemetry.ActivitySource.StartActivity("ingestion.process");
        activity?.SetTag("documind.document_id", job.DocumentId);
        activity?.SetTag("documind.workspace_id", job.WorkspaceId);
        activity?.SetTag("documind.attempt", job.Attempts);

        var document = await db.Documents.FirstOrDefaultAsync(d => d.Id == job.DocumentId, ct);
        if (document is null)
        {
            job.Succeed(); // document was deleted while queued; nothing to do
            await db.SaveChangesAsync(ct);
            return;
        }

        try
        {
            await IndexAsync(document, ct);
            job.Succeed();
            await db.SaveChangesAsync(ct);
            await NotifyAsync(document, ct);
            DocuMindTelemetry.DocumentsIngested.Add(1, new KeyValuePair<string, object?>("status", "indexed"));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogError(ex, "Ingestion failed for document {DocumentId} (attempt {Attempt})", document.Id, job.Attempts);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);

            var willRetry = job.Fail(ex.Message);
            document.MarkFailed(ex.Message, willRetry);
            await db.SaveChangesAsync(CancellationToken.None);
            await NotifyAsync(document, CancellationToken.None);
            DocuMindTelemetry.DocumentsIngested.Add(1, new KeyValuePair<string, object?>("status", willRetry ? "retry" : "failed"));
        }
    }

    private async Task IndexAsync(Document document, CancellationToken ct)
    {
        var settings = await db.Workspaces
            .Where(w => w.Id == document.WorkspaceId)
            .Select(w => w.Settings)
            .SingleAsync(ct);

        await UpdateProgressAsync(document, 5, ct);

        // 1. Parse
        var extension = Path.GetExtension(document.FileName);
        var parser = parsers.FirstOrDefault(p => p.SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            ?? throw new NotSupportedException($"No parser available for '{extension}' files.");

        ParsedDocument parsed;
        await using (var stream = await storage.OpenReadAsync(document.StoragePath, ct))
        {
            parsed = await parser.ParseAsync(stream, document.FileName, ct);
        }

        if (parsed.Sections.Count == 0)
            throw new InvalidOperationException("The document does not contain any extractable text (scanned PDFs need OCR).");
        await UpdateProgressAsync(document, 20, ct);

        // 2. Chunk
        var chunks = await chunkers.Get(settings.ChunkingStrategy).ChunkAsync(parsed, settings, ct);
        logger.LogInformation("Document {DocumentId} split into {ChunkCount} chunks using {Strategy}",
            document.Id, chunks.Count, settings.ChunkingStrategy);
        await UpdateProgressAsync(document, 35, ct);

        // 3. Remove previous version (re-index)
        await vectorStore.DeleteDocumentAsync(document.WorkspaceId, document.Id, ct);
        await db.DocumentChunks.Where(c => c.DocumentId == document.Id).ExecuteDeleteAsync(ct);

        // 4. Embed + upsert in batches
        await vectorStore.EnsureCollectionAsync(await embeddings.GetDimensionsAsync(ct), ct);

        const int batchSize = 32;
        var entities = new List<DocumentChunk>(chunks.Count);
        for (var offset = 0; offset < chunks.Count; offset += batchSize)
        {
            var batch = chunks.Skip(offset).Take(batchSize).ToList();

            // Contextual embedding: prepend document + section so a chunk like "It costs $5" stays findable.
            var embeddingInputs = batch.Select(c => BuildEmbeddingText(document.FileName, c)).ToList();
            var vectors = await embeddings.EmbedDocumentsAsync(embeddingInputs, ct);

            var records = new List<VectorRecord>(batch.Count);
            for (var i = 0; i < batch.Count; i++)
            {
                var chunk = batch[i];
                var id = Guid.CreateVersion7();
                records.Add(new VectorRecord(id, document.WorkspaceId, document.Id, document.FileName, chunk.Index,
                    chunk.Page, chunk.Section, chunk.Content, vectors[i], Bm25SparseEncoder.EncodeDocument(embeddingInputs[i])));
                entities.Add(new DocumentChunk(id, document.Id, document.WorkspaceId, chunk.Index, chunk.Content,
                    chunk.Page, chunk.Section, chunk.TokenCount));
            }

            await vectorStore.UpsertAsync(records, ct);

            var progress = 35 + (int)(60.0 * Math.Min(chunks.Count, offset + batchSize) / chunks.Count);
            await UpdateProgressAsync(document, progress, ct);
        }

        db.DocumentChunks.AddRange(entities);
        document.MarkIndexed(chunks.Count, parsed.PageCount, embeddings.ModelId);
        DocuMindTelemetry.ChunksIndexed.Add(chunks.Count);
    }

    internal static string BuildEmbeddingText(string fileName, TextChunk chunk) =>
        chunk.Section is null
            ? $"{fileName}\n{chunk.Content}"
            : $"{fileName} > {chunk.Section}\n{chunk.Content}";

    private async Task UpdateProgressAsync(Document document, int progress, CancellationToken ct)
    {
        document.MarkProcessing(progress);
        await db.SaveChangesAsync(ct);
        await NotifyAsync(document, ct);
    }

    private Task NotifyAsync(Document document, CancellationToken ct) =>
        notifier.NotifyAsync(new DocumentStatusChanged(document.WorkspaceId, document.Id, document.Status.ToString(),
            document.Progress, document.ChunkCount, document.Error), ct);
}

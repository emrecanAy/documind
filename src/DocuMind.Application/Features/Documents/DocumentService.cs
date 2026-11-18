using System.Security.Cryptography;
using DocuMind.Application.Abstractions;
using DocuMind.Application.Common;
using DocuMind.Application.Features.Ingestion;
using DocuMind.Application.Features.Workspaces;
using DocuMind.Domain.Documents;
using DocuMind.Domain.Ingestion;
using DocuMind.Domain.Workspaces;
using Microsoft.EntityFrameworkCore;

namespace DocuMind.Application.Features.Documents;

public sealed record DocumentDto(Guid Id, string FileName, string ContentType, long SizeBytes, DocumentStatus Status,
    int Progress, string? Error, int ChunkCount, int PageCount, string? EmbeddingModel, DateTimeOffset CreatedAt,
    DateTimeOffset? IndexedAt);

public sealed record ChunkDto(Guid Id, int Index, string Content, int? Page, string? Section, int TokenCount);

public sealed record UploadFile(string FileName, string ContentType, long Length, Func<Stream> OpenReadStream);

public sealed class DocumentService(
    IAppDbContext db,
    ICurrentUser currentUser,
    IWorkspaceAuthorizer authorizer,
    IFileStorage storage,
    IVectorStore vectorStore,
    IIngestionQueue queue,
    IDocumentStatusNotifier notifier)
{
    public const long MaxFileSizeBytes = 25 * 1024 * 1024;

    public async Task<IReadOnlyList<DocumentDto>> ListAsync(Guid workspaceId, CancellationToken ct)
    {
        await authorizer.RequireAsync(workspaceId, WorkspaceRole.Viewer, ct);
        var documents = await db.Documents
            .Where(d => d.WorkspaceId == workspaceId)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(ct);
        return documents.Select(ToDto).ToList();
    }

    public async Task<DocumentDto> GetAsync(Guid workspaceId, Guid documentId, CancellationToken ct)
    {
        await authorizer.RequireAsync(workspaceId, WorkspaceRole.Viewer, ct);
        return ToDto(await LoadAsync(workspaceId, documentId, ct));
    }

    public async Task<IReadOnlyList<ChunkDto>> GetChunksAsync(Guid workspaceId, Guid documentId, CancellationToken ct)
    {
        await authorizer.RequireAsync(workspaceId, WorkspaceRole.Viewer, ct);
        await LoadAsync(workspaceId, documentId, ct);
        return await db.DocumentChunks
            .Where(c => c.DocumentId == documentId && c.WorkspaceId == workspaceId)
            .OrderBy(c => c.Index)
            .Select(c => new ChunkDto(c.Id, c.Index, c.Content, c.Page, c.Section, c.TokenCount))
            .ToListAsync(ct);
    }

    public async Task<(Stream Content, string ContentType, string FileName)> DownloadAsync(Guid workspaceId,
        Guid documentId, CancellationToken ct)
    {
        await authorizer.RequireAsync(workspaceId, WorkspaceRole.Viewer, ct);
        var document = await LoadAsync(workspaceId, documentId, ct);
        return (await storage.OpenReadAsync(document.StoragePath, ct), document.ContentType, document.FileName);
    }

    public async Task<IReadOnlyList<DocumentDto>> UploadAsync(Guid workspaceId, IReadOnlyList<UploadFile> files,
        CancellationToken ct)
    {
        await authorizer.RequireAsync(workspaceId, WorkspaceRole.Editor, ct);
        if (files.Count == 0)
            throw new AppValidationException("files", "At least one file is required.");

        foreach (var file in files)
            Validate(file);

        var created = new List<Document>();
        foreach (var file in files)
        {
            string sha256;
            await using (var hashStream = file.OpenReadStream())
            {
                sha256 = Convert.ToHexStringLower(await SHA256.HashDataAsync(hashStream, ct));
            }

            var duplicate = await db.Documents.AnyAsync(d => d.WorkspaceId == workspaceId && d.Sha256 == sha256, ct);
            if (duplicate)
                throw new ConflictException($"'{file.FileName}' has already been uploaded to this workspace.");

            string path;
            await using (var content = file.OpenReadStream())
            {
                path = await storage.SaveAsync(workspaceId, file.FileName, content, ct);
            }

            var document = Document.Create(workspaceId, Path.GetFileName(file.FileName), ResolveContentType(file),
                file.Length, path, sha256, currentUser.UserId);
            db.Documents.Add(document);
            db.IngestionJobs.Add(IngestionJob.Create(document.Id, workspaceId));
            created.Add(document);
        }

        await db.SaveChangesAsync(ct);
        await queue.SignalAsync(ct);
        foreach (var document in created)
            await NotifyAsync(document, ct);

        return created.Select(ToDto).ToList();
    }

    public async Task<DocumentDto> ReindexAsync(Guid workspaceId, Guid documentId, CancellationToken ct)
    {
        await authorizer.RequireAsync(workspaceId, WorkspaceRole.Editor, ct);
        var document = await LoadAsync(workspaceId, documentId, ct);
        await EnqueueAsync([document], ct);
        return ToDto(document);
    }

    public async Task<int> ReindexWorkspaceAsync(Guid workspaceId, CancellationToken ct)
    {
        await authorizer.RequireAsync(workspaceId, WorkspaceRole.Editor, ct);
        var documents = await db.Documents.Where(d => d.WorkspaceId == workspaceId).ToListAsync(ct);
        await EnqueueAsync(documents, ct);
        return documents.Count;
    }

    public async Task DeleteAsync(Guid workspaceId, Guid documentId, CancellationToken ct)
    {
        await authorizer.RequireAsync(workspaceId, WorkspaceRole.Editor, ct);
        var document = await LoadAsync(workspaceId, documentId, ct);

        await vectorStore.DeleteDocumentAsync(workspaceId, documentId, ct);
        await db.DocumentChunks.Where(c => c.DocumentId == documentId).ExecuteDeleteAsync(ct);
        await db.IngestionJobs.Where(j => j.DocumentId == documentId).ExecuteDeleteAsync(ct);
        db.Documents.Remove(document);
        await db.SaveChangesAsync(ct);
        await storage.DeleteAsync(document.StoragePath, ct);
    }

    private async Task EnqueueAsync(IReadOnlyList<Document> documents, CancellationToken ct)
    {
        if (documents.Count == 0)
            return;

        var ids = documents.Select(d => d.Id).ToList();
        var hasActiveJob = await db.IngestionJobs
            .Where(j => ids.Contains(j.DocumentId) && (j.Status == IngestionJobStatus.Pending || j.Status == IngestionJobStatus.Running))
            .Select(j => j.DocumentId)
            .ToListAsync(ct);

        foreach (var document in documents.Where(d => !hasActiveJob.Contains(d.Id)))
        {
            document.MarkQueued();
            db.IngestionJobs.Add(IngestionJob.Create(document.Id, document.WorkspaceId));
        }

        await db.SaveChangesAsync(ct);
        await queue.SignalAsync(ct);
        foreach (var document in documents)
            await NotifyAsync(document, ct);
    }

    private async Task<Document> LoadAsync(Guid workspaceId, Guid documentId, CancellationToken ct) =>
        await db.Documents.FirstOrDefaultAsync(d => d.Id == documentId && d.WorkspaceId == workspaceId, ct)
        ?? throw new NotFoundException("Document", documentId);

    private static void Validate(UploadFile file)
    {
        var extension = Path.GetExtension(file.FileName);
        if (!IngestionPipeline.SupportedExtensions.Contains(extension))
            throw new AppValidationException("files",
                $"'{file.FileName}': unsupported file type. Allowed: {string.Join(", ", IngestionPipeline.SupportedExtensions)}");
        if (file.Length == 0)
            throw new AppValidationException("files", $"'{file.FileName}' is empty.");
        if (file.Length > MaxFileSizeBytes)
            throw new AppValidationException("files", $"'{file.FileName}' exceeds the {MaxFileSizeBytes / 1024 / 1024} MB limit.");
    }

    private static string ResolveContentType(UploadFile file) => Path.GetExtension(file.FileName).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".md" or ".markdown" => "text/markdown",
        ".html" or ".htm" => "text/html",
        ".txt" => "text/plain",
        _ => string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
    };

    private Task NotifyAsync(Document d, CancellationToken ct) =>
        notifier.NotifyAsync(new DocumentStatusChanged(d.WorkspaceId, d.Id, d.Status.ToString(), d.Progress, d.ChunkCount, d.Error), ct);

    internal static DocumentDto ToDto(Document d) =>
        new(d.Id, d.FileName, d.ContentType, d.SizeBytes, d.Status, d.Progress, d.Error, d.ChunkCount, d.PageCount,
            d.EmbeddingModel, d.CreatedAt, d.IndexedAt);
}

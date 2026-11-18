using DocuMind.Application.Features.Ingestion;
using DocuMind.Domain.Ingestion;

namespace DocuMind.Application.Abstractions;

public interface ICurrentUser
{
    Guid UserId { get; }
    bool IsAuthenticated { get; }
}

public interface IFileStorage
{
    Task<string> SaveAsync(Guid workspaceId, string fileName, Stream content, CancellationToken ct);
    Task<Stream> OpenReadAsync(string path, CancellationToken ct);
    Task DeleteAsync(string path, CancellationToken ct);
}

public interface IDocumentParser
{
    IReadOnlyCollection<string> SupportedExtensions { get; }
    Task<ParsedDocument> ParseAsync(Stream content, string fileName, CancellationToken ct);
}

/// <summary>Postgres-backed work queue. Jobs are claimed atomically so many workers can run in parallel.</summary>
public interface IIngestionQueue
{
    Task<IngestionJob?> ClaimNextAsync(CancellationToken ct);
    Task SignalAsync(CancellationToken ct);
    Task WaitForSignalAsync(TimeSpan timeout, CancellationToken ct);
}

public sealed record DocumentStatusChanged(Guid WorkspaceId, Guid DocumentId, string Status, int Progress,
    int ChunkCount, string? Error);

public interface IDocumentStatusNotifier
{
    Task NotifyAsync(DocumentStatusChanged change, CancellationToken ct);
}

public sealed record UserInfo(Guid Id, string Email, string DisplayName);

public interface IUserDirectory
{
    Task<UserInfo?> FindByEmailAsync(string email, CancellationToken ct);
    Task<IReadOnlyDictionary<Guid, UserInfo>> GetUsersAsync(IEnumerable<Guid> ids, CancellationToken ct);
}

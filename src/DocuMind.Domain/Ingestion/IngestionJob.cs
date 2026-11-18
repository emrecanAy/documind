using DocuMind.Domain.Common;

namespace DocuMind.Domain.Ingestion;

public enum IngestionJobStatus
{
    Pending,
    Running,
    Succeeded,
    DeadLetter,
}

/// <summary>A unit of work in the Postgres-backed ingestion queue (claimed with FOR UPDATE SKIP LOCKED).</summary>
public sealed class IngestionJob : Entity
{
    public const int MaxAttempts = 3;

    private IngestionJob() { }

    public Guid DocumentId { get; private init; }
    public Guid WorkspaceId { get; private init; }
    public IngestionJobStatus Status { get; private set; }
    public int Attempts { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }
    public DateTimeOffset? LockedUntil { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public string? LastError { get; private set; }

    public static IngestionJob Create(Guid documentId, Guid workspaceId) => new()
    {
        DocumentId = documentId,
        WorkspaceId = workspaceId,
        Status = IngestionJobStatus.Pending,
        NextAttemptAt = DateTimeOffset.UtcNow,
    };

    public void Succeed()
    {
        Status = IngestionJobStatus.Succeeded;
        CompletedAt = DateTimeOffset.UtcNow;
        LockedUntil = null;
        LastError = null;
    }

    /// <returns>true when the job will be retried, false when it was dead-lettered.</returns>
    public bool Fail(string error)
    {
        LastError = error.Length > 2000 ? error[..2000] : error;
        LockedUntil = null;
        if (Attempts >= MaxAttempts)
        {
            Status = IngestionJobStatus.DeadLetter;
            CompletedAt = DateTimeOffset.UtcNow;
            return false;
        }

        Status = IngestionJobStatus.Pending;
        NextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(Math.Pow(2, Attempts) * 10); // exponential backoff
        return true;
    }
}

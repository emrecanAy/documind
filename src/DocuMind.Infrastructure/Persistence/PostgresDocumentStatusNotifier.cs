using System.Text.Json;
using DocuMind.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DocuMind.Infrastructure.Persistence;

/// <summary>
/// Publishes document status changes with Postgres NOTIFY. The API listens on the channel and fans the events
/// out to browsers through SignalR, so the worker needs no direct connection to the web tier.
/// </summary>
internal sealed class PostgresDocumentStatusNotifier(AppDbContext db, ILogger<PostgresDocumentStatusNotifier> logger)
    : IDocumentStatusNotifier
{
    public const string Channel = "document_status";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task NotifyAsync(DocumentStatusChanged change, CancellationToken ct)
    {
        try
        {
            var payload = JsonSerializer.Serialize(change, JsonOptions);
            await db.Database.ExecuteSqlAsync($"SELECT pg_notify({Channel}, {payload})", ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Notifications are best effort; the UI also polls.
            logger.LogWarning(ex, "Failed to publish status for document {DocumentId}", change.DocumentId);
        }
    }
}

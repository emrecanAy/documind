using DocuMind.Application.Abstractions;
using DocuMind.Domain.Ingestion;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DocuMind.Infrastructure.Persistence;

/// <summary>
/// Transactional job queue on Postgres. <c>FOR UPDATE SKIP LOCKED</c> lets any number of workers claim jobs
/// concurrently without double processing; a lease (<c>locked_until</c>) recovers jobs from crashed workers.
/// LISTEN/NOTIFY wakes idle workers immediately instead of waiting for the next poll.
/// </summary>
internal sealed class PostgresIngestionQueue(AppDbContext db) : IIngestionQueue
{
    public const string Channel = "ingestion_jobs";

    private const string ClaimSql = """
        UPDATE ingestion_jobs
        SET status = 'Running', attempts = attempts + 1, locked_until = now() + interval '15 minutes'
        WHERE id = (
            SELECT id FROM ingestion_jobs
            WHERE (status = 'Pending' AND next_attempt_at <= now())
               OR (status = 'Running' AND locked_until < now())
            ORDER BY created_at
            FOR UPDATE SKIP LOCKED
            LIMIT 1)
        RETURNING *
        """;

    public async Task<IngestionJob?> ClaimNextAsync(CancellationToken ct)
    {
        var jobs = await db.IngestionJobs.FromSqlRaw(ClaimSql).AsTracking().ToListAsync(ct);
        return jobs.SingleOrDefault();
    }

    public Task SignalAsync(CancellationToken ct) =>
        db.Database.ExecuteSqlRawAsync($"SELECT pg_notify('{Channel}', '')", ct);

    public async Task WaitForSignalAsync(TimeSpan timeout, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(db.Database.GetConnectionString());
        await connection.OpenAsync(ct);
        await using (var listen = new NpgsqlCommand($"LISTEN {Channel}", connection))
        {
            await listen.ExecuteNonQueryAsync(ct);
        }

        await connection.WaitAsync(timeout, ct);
    }
}

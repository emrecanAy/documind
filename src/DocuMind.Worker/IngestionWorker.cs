using DocuMind.Application.Abstractions;
using DocuMind.Application.Features.Ingestion;
using Microsoft.Extensions.Options;

namespace DocuMind.Worker;

public sealed class IngestionWorkerOptions
{
    public const string SectionName = "Ingestion";

    /// <summary>Number of documents processed in parallel by this worker instance.</summary>
    public int Concurrency { get; set; } = 2;

    /// <summary>Fallback poll interval when no NOTIFY arrives.</summary>
    public int IdlePollSeconds { get; set; } = 5;
}

internal sealed class SystemUser : ICurrentUser
{
    public Guid UserId => Guid.Empty;
    public bool IsAuthenticated => false;
}

/// <summary>
/// Pulls jobs from the Postgres queue and runs the ingestion pipeline. Scale horizontally by running more
/// instances (or raising Concurrency): SKIP LOCKED guarantees each job is processed by exactly one consumer.
/// </summary>
public sealed class IngestionWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<IngestionWorkerOptions> options,
    ILogger<IngestionWorker> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Ingestion worker started with concurrency {Concurrency}", options.Value.Concurrency);
        var consumers = Enumerable.Range(0, Math.Max(1, options.Value.Concurrency))
            .Select(i => ConsumeAsync(i, stoppingToken));
        return Task.WhenAll(consumers);
    }

    private async Task ConsumeAsync(int consumerId, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var processed = await ProcessNextAsync(ct);
                if (!processed)
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var queue = scope.ServiceProvider.GetRequiredService<IIngestionQueue>();
                    await queue.WaitForSignalAsync(TimeSpan.FromSeconds(options.Value.IdlePollSeconds), ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Consumer {ConsumerId} crashed, backing off", consumerId);
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
        }
    }

    private async Task<bool> ProcessNextAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var queue = scope.ServiceProvider.GetRequiredService<IIngestionQueue>();
        var job = await queue.ClaimNextAsync(ct);
        if (job is null)
            return false;

        logger.LogInformation("Processing document {DocumentId} (attempt {Attempt})", job.DocumentId, job.Attempts);
        var pipeline = scope.ServiceProvider.GetRequiredService<IngestionPipeline>();
        await pipeline.ProcessAsync(job, ct);
        return true;
    }
}

using DocuMind.Application.Features.Evaluation;
using DocuMind.Domain.Evaluation;
using DocuMind.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DocuMind.Api.Background;

/// <summary>Executes evaluation runs one at a time so they do not starve interactive chat of model capacity.</summary>
internal sealed class EvalBackgroundService(
    IEvalQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<EvalBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverAsync(stoppingToken);

        await foreach (var runId in queue.DequeueAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<EvalRunner>().RunAsync(runId, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Evaluation run {RunId} crashed", runId);
            }
        }
    }

    /// <summary>Re-queues runs that were pending when the process stopped.</summary>
    private async Task RecoverAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var pending = await db.EvalRuns.Where(r => r.Status == EvalRunStatus.Pending).Select(r => r.Id).ToListAsync(ct);
            foreach (var id in pending)
                await queue.EnqueueAsync(id, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not recover pending evaluation runs");
        }
    }
}

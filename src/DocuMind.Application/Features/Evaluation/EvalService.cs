using System.Text.Json;
using System.Text.Json.Serialization;
using DocuMind.Application.Abstractions;
using DocuMind.Application.Common;
using DocuMind.Application.Features.Workspaces;
using DocuMind.Domain.Evaluation;
using DocuMind.Domain.Workspaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DocuMind.Application.Features.Evaluation;

public sealed class EvalService(
    IAppDbContext db,
    ICurrentUser currentUser,
    IWorkspaceAuthorizer authorizer,
    IEvalQueue queue,
    IOptions<AiOptions> aiOptions)
{
    private static readonly JsonSerializerOptions SnapshotJson = new() { Converters = { new JsonStringEnumConverter() } };

    public async Task<EvalRunSummary> StartAsync(Guid workspaceId, StartEvalRequest request, CancellationToken ct)
    {
        await authorizer.RequireAsync(workspaceId, WorkspaceRole.Editor, ct);
        var settings = await db.Workspaces.Where(w => w.Id == workspaceId).Select(w => w.Settings).SingleAsync(ct);

        // Snapshot the configuration so runs can be compared later (e.g. Recursive vs Semantic chunking).
        var snapshot = JsonSerializer.Serialize(new
        {
            settings.ChunkingStrategy,
            settings.ChunkSize,
            settings.ChunkOverlap,
            settings.TopK,
            settings.UseHybridSearch,
            settings.UseReranker,
            settings.UseQueryExpansion,
            aiOptions.Value.ChatModel,
            aiOptions.Value.EmbeddingModel,
        }, SnapshotJson);

        var run = EvalRun.Create(workspaceId, currentUser.UserId, request.Dataset.Name,
            JsonSerializer.Serialize(request.Dataset), request.UseLlmJudge, snapshot);
        db.EvalRuns.Add(run);
        await db.SaveChangesAsync(ct);
        await queue.EnqueueAsync(run.Id, ct);
        return ToSummary(run);
    }

    public async Task<IReadOnlyList<EvalRunSummary>> ListAsync(Guid workspaceId, CancellationToken ct)
    {
        await authorizer.RequireAsync(workspaceId, WorkspaceRole.Viewer, ct);
        var runs = await db.EvalRuns
            .Where(r => r.WorkspaceId == workspaceId)
            .OrderByDescending(r => r.CreatedAt)
            .Take(50)
            .ToListAsync(ct);
        return runs.Select(ToSummary).ToList();
    }

    public async Task<EvalRunDetails> GetAsync(Guid workspaceId, Guid runId, CancellationToken ct)
    {
        await authorizer.RequireAsync(workspaceId, WorkspaceRole.Viewer, ct);
        var run = await db.EvalRuns.FirstOrDefaultAsync(r => r.Id == runId && r.WorkspaceId == workspaceId, ct)
                  ?? throw new NotFoundException("Evaluation run", runId);
        return new EvalRunDetails(ToSummary(run), run.Results);
    }

    private static EvalRunSummary ToSummary(EvalRun r) =>
        new(r.Id, r.DatasetName, r.Status, r.UseLlmJudge, r.Metrics, r.SettingsSnapshot, r.Error, r.CreatedAt, r.CompletedAt);
}

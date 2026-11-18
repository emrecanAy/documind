using System.Text.Json;
using DocuMind.Api.Infrastructure;
using DocuMind.Application.Common;
using DocuMind.Application.Features.Evaluation;
using Microsoft.Extensions.Options;

namespace DocuMind.Api.Endpoints;

internal static class EvalEndpoints
{
    public static IEndpointRouteBuilder MapEvalEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/workspaces/{workspaceId:guid}/evals").WithTags("Evaluation").RequireAuthorization();

        group.MapGet("/", async (Guid workspaceId, EvalService service, CancellationToken ct) =>
            TypedResults.Ok(await service.ListAsync(workspaceId, ct)));

        group.MapPost("/", async (Guid workspaceId, StartEvalRequest request, EvalService service, CancellationToken ct) =>
            {
                var run = await service.StartAsync(workspaceId, request, ct);
                return TypedResults.Accepted($"/api/workspaces/{workspaceId}/evals/{run.Id}", run);
            })
            .Validate<StartEvalRequest>();

        group.MapGet("/{runId:guid}", async (Guid workspaceId, Guid runId, EvalService service, CancellationToken ct) =>
            TypedResults.Ok(await service.GetAsync(workspaceId, runId, ct)));

        var system = app.MapGroup("/api/system").WithTags("System").RequireAuthorization();

        // Sample golden dataset that matches samples/docs, so evaluation works out of the box.
        system.MapGet("/sample-dataset", async (CancellationToken ct) =>
        {
            var path = Path.Combine(AppContext.BaseDirectory, "SampleData", "sample-golden.json");
            if (!File.Exists(path))
                return Results.NotFound();
            await using var stream = File.OpenRead(path);
            var dataset = await JsonSerializer.DeserializeAsync<EvalDataset>(stream, JsonSerializerOptions.Web, ct);
            return Results.Ok(dataset);
        });

        system.MapGet("/info", (IOptions<AiOptions> ai) => TypedResults.Ok(new
        {
            ai.Value.ChatProvider,
            ai.Value.ChatModel,
            ai.Value.EmbeddingProvider,
            ai.Value.EmbeddingModel,
            VectorStore = "Qdrant (dense + BM25 sparse, RRF fusion)",
            SupportedFileTypes = DocuMind.Application.Features.Ingestion.IngestionPipeline.SupportedExtensions.Order(),
        }));

        return app;
    }
}

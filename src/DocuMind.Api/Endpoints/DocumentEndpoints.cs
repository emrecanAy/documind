using DocuMind.Application.Features.Documents;

namespace DocuMind.Api.Endpoints;

internal static class DocumentEndpoints
{
    public static IEndpointRouteBuilder MapDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/workspaces/{workspaceId:guid}/documents").WithTags("Documents").RequireAuthorization();

        group.MapGet("/", async (Guid workspaceId, DocumentService service, CancellationToken ct) =>
            TypedResults.Ok(await service.ListAsync(workspaceId, ct)));

        group.MapPost("/", async (Guid workspaceId, IFormFileCollection files, DocumentService service, CancellationToken ct) =>
            {
                var uploads = files
                    .Select(f => new UploadFile(f.FileName, f.ContentType, f.Length, f.OpenReadStream))
                    .ToList();
                return TypedResults.Ok(await service.UploadAsync(workspaceId, uploads, ct));
            })
            .DisableAntiforgery() // JWT bearer API: no cookie auth, so no CSRF surface
            .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(110 * 1024 * 1024));

        group.MapPost("/reindex", async (Guid workspaceId, DocumentService service, CancellationToken ct) =>
            TypedResults.Ok(new { queued = await service.ReindexWorkspaceAsync(workspaceId, ct) }));

        group.MapGet("/{documentId:guid}", async (Guid workspaceId, Guid documentId, DocumentService service, CancellationToken ct) =>
            TypedResults.Ok(await service.GetAsync(workspaceId, documentId, ct)));

        group.MapGet("/{documentId:guid}/chunks", async (Guid workspaceId, Guid documentId, DocumentService service,
            CancellationToken ct) => TypedResults.Ok(await service.GetChunksAsync(workspaceId, documentId, ct)));

        group.MapGet("/{documentId:guid}/download", async (Guid workspaceId, Guid documentId, DocumentService service,
            CancellationToken ct) =>
        {
            var (content, contentType, fileName) = await service.DownloadAsync(workspaceId, documentId, ct);
            return TypedResults.File(content, contentType, fileName);
        });

        group.MapPost("/{documentId:guid}/reindex", async (Guid workspaceId, Guid documentId, DocumentService service,
            CancellationToken ct) => TypedResults.Ok(await service.ReindexAsync(workspaceId, documentId, ct)));

        group.MapDelete("/{documentId:guid}", async (Guid workspaceId, Guid documentId, DocumentService service,
            CancellationToken ct) =>
        {
            await service.DeleteAsync(workspaceId, documentId, ct);
            return TypedResults.NoContent();
        });

        return app;
    }
}

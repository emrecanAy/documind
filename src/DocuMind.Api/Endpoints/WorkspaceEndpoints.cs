using DocuMind.Api.Infrastructure;
using DocuMind.Application.Features.Chat;
using DocuMind.Application.Features.Workspaces;
using DocuMind.Domain.Workspaces;

namespace DocuMind.Api.Endpoints;

internal static class WorkspaceEndpoints
{
    public static IEndpointRouteBuilder MapWorkspaceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/workspaces").WithTags("Workspaces").RequireAuthorization();

        group.MapGet("/", async (WorkspaceService service, CancellationToken ct) =>
            TypedResults.Ok(await service.ListAsync(ct)));

        group.MapPost("/", async (CreateWorkspaceRequest request, WorkspaceService service, CancellationToken ct) =>
            {
                var workspace = await service.CreateAsync(request, ct);
                return TypedResults.Created($"/api/workspaces/{workspace.Id}", workspace);
            })
            .Validate<CreateWorkspaceRequest>();

        group.MapGet("/{workspaceId:guid}", async (Guid workspaceId, WorkspaceService service, CancellationToken ct) =>
            TypedResults.Ok(await service.GetAsync(workspaceId, ct)));

        group.MapPut("/{workspaceId:guid}", async (Guid workspaceId, UpdateWorkspaceRequest request, WorkspaceService service,
                CancellationToken ct) => TypedResults.Ok(await service.UpdateAsync(workspaceId, request, ct)))
            .Validate<UpdateWorkspaceRequest>();

        group.MapDelete("/{workspaceId:guid}", async (Guid workspaceId, WorkspaceService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(workspaceId, ct);
            return TypedResults.NoContent();
        });

        group.MapPut("/{workspaceId:guid}/settings", async (Guid workspaceId, RagSettings settings, bool? reindex,
                WorkspaceService service, CancellationToken ct) =>
                TypedResults.Ok(await service.UpdateSettingsAsync(workspaceId, settings, reindex ?? false, ct)))
            .Validate<RagSettings>();

        group.MapPost("/{workspaceId:guid}/members", async (Guid workspaceId, AddMemberRequest request, WorkspaceService service,
                CancellationToken ct) => TypedResults.Ok(await service.AddMemberAsync(workspaceId, request, ct)))
            .Validate<AddMemberRequest>();

        group.MapDelete("/{workspaceId:guid}/members/{userId:guid}", async (Guid workspaceId, Guid userId,
            WorkspaceService service, CancellationToken ct) =>
        {
            await service.RemoveMemberAsync(workspaceId, userId, ct);
            return TypedResults.NoContent();
        });

        group.MapGet("/{workspaceId:guid}/usage", async (Guid workspaceId, ConversationService service, CancellationToken ct) =>
            TypedResults.Ok(await service.GetUsageAsync(workspaceId, ct)));

        return app;
    }
}

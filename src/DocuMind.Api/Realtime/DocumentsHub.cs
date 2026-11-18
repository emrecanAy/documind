using DocuMind.Application.Features.Workspaces;
using DocuMind.Domain.Workspaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace DocuMind.Api.Realtime;

/// <summary>Pushes live ingestion progress to browsers. Clients join one group per workspace they are viewing.</summary>
[Authorize]
public sealed class DocumentsHub(IWorkspaceAuthorizer authorizer) : Hub
{
    public const string Path = "/hubs/documents";

    public static string GroupName(Guid workspaceId) => $"workspace:{workspaceId:N}";

    public async Task JoinWorkspace(Guid workspaceId)
    {
        await authorizer.RequireAsync(workspaceId, WorkspaceRole.Viewer, Context.ConnectionAborted);
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(workspaceId));
    }

    public Task LeaveWorkspace(Guid workspaceId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(workspaceId));
}

using DocuMind.Application.Abstractions;
using DocuMind.Application.Common;
using DocuMind.Domain.Workspaces;
using Microsoft.EntityFrameworkCore;

namespace DocuMind.Application.Features.Workspaces;

public interface IWorkspaceAuthorizer
{
    /// <summary>Ensures the current user is a member with at least <paramref name="minimumRole"/>; returns the role.</summary>
    Task<WorkspaceRole> RequireAsync(Guid workspaceId, WorkspaceRole minimumRole, CancellationToken ct);
}

public sealed class WorkspaceAuthorizer(IAppDbContext db, ICurrentUser currentUser) : IWorkspaceAuthorizer
{
    public async Task<WorkspaceRole> RequireAsync(Guid workspaceId, WorkspaceRole minimumRole, CancellationToken ct)
    {
        var role = await db.WorkspaceMembers
            .Where(m => m.WorkspaceId == workspaceId && m.UserId == currentUser.UserId)
            .Select(m => (WorkspaceRole?)m.Role)
            .SingleOrDefaultAsync(ct);

        // Non-members get 404 (not 403) so workspace ids cannot be probed.
        if (role is null)
            throw new NotFoundException("Workspace", workspaceId);
        if (role < minimumRole)
            throw new ForbiddenException($"This action requires the '{minimumRole}' role.");
        return role.Value;
    }
}

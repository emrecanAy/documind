using DocuMind.Application.Abstractions;
using DocuMind.Application.Common;
using DocuMind.Application.Features.Documents;
using DocuMind.Domain.Workspaces;
using Microsoft.EntityFrameworkCore;

namespace DocuMind.Application.Features.Workspaces;

public sealed class WorkspaceService(
    IAppDbContext db,
    ICurrentUser currentUser,
    IWorkspaceAuthorizer authorizer,
    IUserDirectory users,
    IVectorStore vectorStore,
    IFileStorage storage,
    DocumentService documents)
{
    public async Task<IReadOnlyList<WorkspaceSummary>> ListAsync(CancellationToken ct)
    {
        var userId = currentUser.UserId;
        return await db.WorkspaceMembers
            .Where(m => m.UserId == userId)
            .Join(db.Workspaces, m => m.WorkspaceId, w => w.Id, (m, w) => new { m.Role, Workspace = w })
            .OrderBy(x => x.Workspace.Name)
            .Select(x => new WorkspaceSummary(
                x.Workspace.Id,
                x.Workspace.Name,
                x.Workspace.Description,
                x.Role,
                db.Documents.Count(d => d.WorkspaceId == x.Workspace.Id),
                x.Workspace.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<WorkspaceDetails> GetAsync(Guid workspaceId, CancellationToken ct)
    {
        var role = await authorizer.RequireAsync(workspaceId, WorkspaceRole.Viewer, ct);
        var workspace = await LoadAsync(workspaceId, ct);
        return await ToDetailsAsync(workspace, role, ct);
    }

    public async Task<WorkspaceDetails> CreateAsync(CreateWorkspaceRequest request, CancellationToken ct)
    {
        var workspace = Workspace.Create(request.Name, request.Description, currentUser.UserId);
        db.Workspaces.Add(workspace);
        await db.SaveChangesAsync(ct);
        return await ToDetailsAsync(workspace, WorkspaceRole.Owner, ct);
    }

    public async Task<WorkspaceDetails> UpdateAsync(Guid workspaceId, UpdateWorkspaceRequest request, CancellationToken ct)
    {
        var role = await authorizer.RequireAsync(workspaceId, WorkspaceRole.Owner, ct);
        var workspace = await LoadAsync(workspaceId, ct);
        workspace.Rename(request.Name, request.Description);
        await db.SaveChangesAsync(ct);
        return await ToDetailsAsync(workspace, role, ct);
    }

    /// <summary>Updates RAG settings. Chunking changes only take effect after a re-index, which can be requested.</summary>
    public async Task<WorkspaceDetails> UpdateSettingsAsync(Guid workspaceId, RagSettings settings, bool reindex,
        CancellationToken ct)
    {
        var role = await authorizer.RequireAsync(workspaceId, WorkspaceRole.Owner, ct);
        var workspace = await LoadAsync(workspaceId, ct);
        workspace.UpdateSettings(settings);
        await db.SaveChangesAsync(ct);

        if (reindex)
            await documents.ReindexWorkspaceAsync(workspaceId, ct);
        return await ToDetailsAsync(workspace, role, ct);
    }

    public async Task DeleteAsync(Guid workspaceId, CancellationToken ct)
    {
        await authorizer.RequireAsync(workspaceId, WorkspaceRole.Owner, ct);
        var workspace = await LoadAsync(workspaceId, ct);

        var paths = await db.Documents.Where(d => d.WorkspaceId == workspaceId).Select(d => d.StoragePath).ToListAsync(ct);
        await vectorStore.DeleteWorkspaceAsync(workspaceId, ct);

        await db.DocumentChunks.Where(c => c.WorkspaceId == workspaceId).ExecuteDeleteAsync(ct);
        await db.IngestionJobs.Where(j => j.WorkspaceId == workspaceId).ExecuteDeleteAsync(ct);
        await db.Documents.Where(d => d.WorkspaceId == workspaceId).ExecuteDeleteAsync(ct);
        await db.EvalRuns.Where(r => r.WorkspaceId == workspaceId).ExecuteDeleteAsync(ct);
        await db.Conversations.Where(c => c.WorkspaceId == workspaceId).ExecuteDeleteAsync(ct);
        db.Workspaces.Remove(workspace);
        await db.SaveChangesAsync(ct);

        foreach (var path in paths)
            await storage.DeleteAsync(path, ct);
    }

    public async Task<MemberDto> AddMemberAsync(Guid workspaceId, AddMemberRequest request, CancellationToken ct)
    {
        await authorizer.RequireAsync(workspaceId, WorkspaceRole.Owner, ct);
        var user = await users.FindByEmailAsync(request.Email, ct)
            ?? throw new AppValidationException(nameof(request.Email), "No user is registered with this email.");

        var workspace = await LoadAsync(workspaceId, ct);
        if (workspace.Members.Any(m => m.UserId == user.Id))
            throw new ConflictException("User is already a member of this workspace.");

        var member = workspace.AddMember(user.Id, request.Role);
        await db.SaveChangesAsync(ct);
        return new MemberDto(user.Id, user.Email, user.DisplayName, member.Role, member.JoinedAt);
    }

    public async Task RemoveMemberAsync(Guid workspaceId, Guid userId, CancellationToken ct)
    {
        // members may leave on their own; removing others requires Owner
        await authorizer.RequireAsync(workspaceId, userId == currentUser.UserId ? WorkspaceRole.Viewer : WorkspaceRole.Owner, ct);
        var workspace = await LoadAsync(workspaceId, ct);
        workspace.RemoveMember(userId);
        await db.SaveChangesAsync(ct);
    }

    private async Task<Workspace> LoadAsync(Guid workspaceId, CancellationToken ct) =>
        await db.Workspaces.Include(w => w.Members).FirstOrDefaultAsync(w => w.Id == workspaceId, ct)
        ?? throw new NotFoundException("Workspace", workspaceId);

    private async Task<WorkspaceDetails> ToDetailsAsync(Workspace workspace, WorkspaceRole role, CancellationToken ct)
    {
        var userInfos = await users.GetUsersAsync(workspace.Members.Select(m => m.UserId), ct);
        var members = workspace.Members
            .Select(m =>
            {
                var info = userInfos.GetValueOrDefault(m.UserId);
                return new MemberDto(m.UserId, info?.Email ?? "unknown", info?.DisplayName ?? "Unknown user", m.Role, m.JoinedAt);
            })
            .OrderByDescending(m => m.Role)
            .ThenBy(m => m.DisplayName)
            .ToList();

        return new WorkspaceDetails(workspace.Id, workspace.Name, workspace.Description, role, workspace.Settings, members,
            workspace.CreatedAt);
    }
}

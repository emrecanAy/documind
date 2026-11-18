namespace DocuMind.Domain.Workspaces;

public sealed class WorkspaceMember
{
    private WorkspaceMember() { }

    internal WorkspaceMember(Guid workspaceId, Guid userId, WorkspaceRole role)
    {
        WorkspaceId = workspaceId;
        UserId = userId;
        Role = role;
    }

    public Guid WorkspaceId { get; private init; }
    public Guid UserId { get; private init; }
    public WorkspaceRole Role { get; private set; }
    public DateTimeOffset JoinedAt { get; private init; } = DateTimeOffset.UtcNow;

    public void ChangeRole(WorkspaceRole role) => Role = role;
}

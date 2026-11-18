namespace DocuMind.Domain.Workspaces;

/// <summary>Ordered by privilege: a higher value includes all lower permissions.</summary>
public enum WorkspaceRole
{
    Viewer = 0,
    Editor = 1,
    Owner = 2,
}

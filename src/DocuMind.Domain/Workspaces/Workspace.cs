using DocuMind.Domain.Common;

namespace DocuMind.Domain.Workspaces;

public sealed class Workspace : Entity
{
    private readonly List<WorkspaceMember> _members = [];

    private Workspace() { }

    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public Guid CreatedBy { get; private init; }
    public RagSettings Settings { get; private set; } = RagSettings.Default;
    public IReadOnlyCollection<WorkspaceMember> Members => _members;

    public static Workspace Create(string name, string? description, Guid ownerId)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Workspace name is required.");

        var workspace = new Workspace
        {
            Name = name.Trim(),
            Description = description?.Trim(),
            CreatedBy = ownerId,
        };
        workspace._members.Add(new WorkspaceMember(workspace.Id, ownerId, WorkspaceRole.Owner));
        return workspace;
    }

    public void Rename(string name, string? description)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Workspace name is required.");
        Name = name.Trim();
        Description = description?.Trim();
    }

    public void UpdateSettings(RagSettings settings)
    {
        settings.Validate();
        Settings = settings;
    }

    public WorkspaceMember AddMember(Guid userId, WorkspaceRole role)
    {
        if (_members.Any(m => m.UserId == userId))
            throw new DomainException("User is already a member of this workspace.");
        var member = new WorkspaceMember(Id, userId, role);
        _members.Add(member);
        return member;
    }

    public void RemoveMember(Guid userId)
    {
        var member = _members.SingleOrDefault(m => m.UserId == userId)
            ?? throw new DomainException("User is not a member of this workspace.");
        if (member.Role == WorkspaceRole.Owner && _members.Count(m => m.Role == WorkspaceRole.Owner) == 1)
            throw new DomainException("A workspace must keep at least one owner.");
        _members.Remove(member);
    }
}

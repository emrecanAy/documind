using DocuMind.Domain.Workspaces;
using FluentValidation;

namespace DocuMind.Application.Features.Workspaces;

public sealed record CreateWorkspaceRequest(string Name, string? Description);

public sealed record UpdateWorkspaceRequest(string Name, string? Description);

public sealed record AddMemberRequest(string Email, WorkspaceRole Role);

public sealed record WorkspaceSummary(Guid Id, string Name, string? Description, WorkspaceRole Role, int DocumentCount,
    DateTimeOffset CreatedAt);

public sealed record WorkspaceDetails(Guid Id, string Name, string? Description, WorkspaceRole Role, RagSettings Settings,
    IReadOnlyList<MemberDto> Members, DateTimeOffset CreatedAt);

public sealed record MemberDto(Guid UserId, string Email, string DisplayName, WorkspaceRole Role, DateTimeOffset JoinedAt);

public sealed class CreateWorkspaceRequestValidator : AbstractValidator<CreateWorkspaceRequest>
{
    public CreateWorkspaceRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(500);
    }
}

public sealed class UpdateWorkspaceRequestValidator : AbstractValidator<UpdateWorkspaceRequest>
{
    public UpdateWorkspaceRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(500);
    }
}

public sealed class AddMemberRequestValidator : AbstractValidator<AddMemberRequest>
{
    public AddMemberRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Role).IsInEnum();
    }
}

public sealed class RagSettingsValidator : AbstractValidator<RagSettings>
{
    public RagSettingsValidator()
    {
        RuleFor(x => x.ChunkingStrategy).IsInEnum();
        RuleFor(x => x.ChunkSize).InclusiveBetween(64, 4096);
        RuleFor(x => x.ChunkOverlap).GreaterThanOrEqualTo(0).LessThan(x => x.ChunkSize / 2);
        RuleFor(x => x.TopK).InclusiveBetween(1, 20);
        RuleFor(x => x.SemanticBreakpointPercentile).ExclusiveBetween(0.5, 1);
    }
}

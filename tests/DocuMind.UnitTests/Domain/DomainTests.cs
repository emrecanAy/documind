using DocuMind.Domain.Common;
using DocuMind.Domain.Conversations;
using DocuMind.Domain.Ingestion;
using DocuMind.Domain.Workspaces;

namespace DocuMind.UnitTests.Domain;

public sealed class DomainTests
{
    [Fact]
    public void Creating_a_workspace_makes_the_creator_owner()
    {
        var owner = Guid.NewGuid();
        var workspace = Workspace.Create("  HR  ", null, owner);

        workspace.Name.ShouldBe("HR");
        workspace.Members.ShouldHaveSingleItem().Role.ShouldBe(WorkspaceRole.Owner);
    }

    [Fact]
    public void Last_owner_cannot_leave()
    {
        var owner = Guid.NewGuid();
        var workspace = Workspace.Create("HR", null, owner);

        Should.Throw<DomainException>(() => workspace.RemoveMember(owner));
    }

    [Fact]
    public void Duplicate_member_is_rejected()
    {
        var workspace = Workspace.Create("HR", null, Guid.NewGuid());
        var user = Guid.NewGuid();
        workspace.AddMember(user, WorkspaceRole.Viewer);

        Should.Throw<DomainException>(() => workspace.AddMember(user, WorkspaceRole.Editor));
    }

    [Theory]
    [InlineData(32, 0, 5)]      // chunk too small
    [InlineData(512, 300, 5)]   // overlap >= half
    [InlineData(512, 64, 50)]   // top-k too large
    public void Invalid_rag_settings_are_rejected(int size, int overlap, int topK)
    {
        var workspace = Workspace.Create("HR", null, Guid.NewGuid());
        var settings = RagSettings.Default with { ChunkSize = size, ChunkOverlap = overlap, TopK = topK };

        Should.Throw<DomainException>(() => workspace.UpdateSettings(settings));
    }

    [Fact]
    public void Ingestion_job_backs_off_then_dead_letters()
    {
        var job = IngestionJob.Create(Guid.NewGuid(), Guid.NewGuid());
        var attempts = typeof(IngestionJob).GetProperty(nameof(IngestionJob.Attempts))!;

        attempts.SetValue(job, 1);
        job.Fail("boom").ShouldBeTrue();
        job.Status.ShouldBe(IngestionJobStatus.Pending);
        job.NextAttemptAt.ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddSeconds(15));

        attempts.SetValue(job, IngestionJob.MaxAttempts);
        job.Fail("boom").ShouldBeFalse();
        job.Status.ShouldBe(IngestionJobStatus.DeadLetter);
    }

    [Fact]
    public void Feedback_only_on_assistant_messages()
    {
        var conversation = Conversation.Start(Guid.NewGuid(), Guid.NewGuid(), "What is the leave policy?");
        var question = conversation.AddUserMessage("What is the leave policy?");
        var answer = conversation.AddAssistantMessage("20 days [1]", [], [], new MessageUsage("m", 1, 1, 0, 1, 1));

        Should.Throw<DomainException>(() => question.GiveFeedback(1, null));
        Should.Throw<DomainException>(() => answer.GiveFeedback(5, null));
        answer.GiveFeedback(-1, "wrong number");
        answer.Feedback.ShouldBe(-1);
    }

    [Fact]
    public void Conversation_title_is_truncated()
    {
        var conversation = Conversation.Start(Guid.NewGuid(), Guid.NewGuid(), new string('x', 200));
        conversation.Title.Length.ShouldBe(80);
    }
}

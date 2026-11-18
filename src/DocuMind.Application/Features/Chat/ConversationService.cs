using DocuMind.Application.Abstractions;
using DocuMind.Application.Common;
using DocuMind.Application.Features.Workspaces;
using DocuMind.Domain.Conversations;
using DocuMind.Domain.Workspaces;
using Microsoft.EntityFrameworkCore;

namespace DocuMind.Application.Features.Chat;

public sealed record UsageSummary(int Conversations, int Answers, long InputTokens, long OutputTokens,
    decimal EstimatedCostUsd, double AvgLatencyMs, int PositiveFeedback, int NegativeFeedback);

/// <summary>Conversation history is private to its author, even inside a shared workspace.</summary>
public sealed class ConversationService(IAppDbContext db, ICurrentUser currentUser, IWorkspaceAuthorizer authorizer)
{
    public async Task<IReadOnlyList<ConversationSummary>> ListAsync(Guid workspaceId, CancellationToken ct)
    {
        await authorizer.RequireAsync(workspaceId, WorkspaceRole.Viewer, ct);
        return await db.Conversations
            .Where(c => c.WorkspaceId == workspaceId && c.UserId == currentUser.UserId)
            .OrderByDescending(c => c.UpdatedAt)
            .Take(100)
            .Select(c => new ConversationSummary(c.Id, c.Title, c.CreatedAt, c.UpdatedAt))
            .ToListAsync(ct);
    }

    public async Task<ConversationDetails> GetAsync(Guid workspaceId, Guid conversationId, CancellationToken ct)
    {
        await authorizer.RequireAsync(workspaceId, WorkspaceRole.Viewer, ct);
        var conversation = await LoadAsync(workspaceId, conversationId, ct);
        var messages = await db.Messages
            .Where(m => m.ConversationId == conversation.Id)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct);

        return new ConversationDetails(conversation.Id, conversation.Title, messages.Select(m =>
            new MessageDto(m.Id, m.Role, m.Content, m.Citations, m.AgentSteps, m.Usage, m.Feedback, m.CreatedAt)).ToList());
    }

    public async Task DeleteAsync(Guid workspaceId, Guid conversationId, CancellationToken ct)
    {
        await authorizer.RequireAsync(workspaceId, WorkspaceRole.Viewer, ct);
        var conversation = await LoadAsync(workspaceId, conversationId, ct);
        db.Conversations.Remove(conversation);
        await db.SaveChangesAsync(ct);
    }

    public async Task GiveFeedbackAsync(Guid workspaceId, Guid conversationId, Guid messageId, FeedbackRequest request,
        CancellationToken ct)
    {
        await authorizer.RequireAsync(workspaceId, WorkspaceRole.Viewer, ct);
        var conversation = await LoadAsync(workspaceId, conversationId, ct);
        var message = await db.Messages.FirstOrDefaultAsync(m => m.Id == messageId && m.ConversationId == conversation.Id, ct)
                      ?? throw new NotFoundException("Message", messageId);
        message.GiveFeedback(request.Score, request.Comment);
        await db.SaveChangesAsync(ct);
        DocuMindTelemetry.Feedback.Add(1, new KeyValuePair<string, object?>("score", request.Score));
    }

    /// <summary>Workspace-wide usage analytics (token spend, latency, feedback) for the observability page.</summary>
    public async Task<UsageSummary> GetUsageAsync(Guid workspaceId, CancellationToken ct)
    {
        await authorizer.RequireAsync(workspaceId, WorkspaceRole.Viewer, ct);
        var conversationIds = db.Conversations.Where(c => c.WorkspaceId == workspaceId).Select(c => c.Id);
        var answers = await db.Messages
            .Where(m => conversationIds.Contains(m.ConversationId) && m.Role == MessageRole.Assistant)
            .Select(m => new { m.Usage, m.Feedback })
            .ToListAsync(ct);

        var usages = answers.Where(a => a.Usage is not null).Select(a => a.Usage!).ToList();
        return new UsageSummary(
            await conversationIds.CountAsync(ct),
            answers.Count,
            usages.Sum(u => (long)u.InputTokens),
            usages.Sum(u => (long)u.OutputTokens),
            usages.Sum(u => u.EstimatedCostUsd),
            usages.Count == 0 ? 0 : usages.Average(u => u.LatencyMs),
            answers.Count(a => a.Feedback == 1),
            answers.Count(a => a.Feedback == -1));
    }

    private async Task<Conversation> LoadAsync(Guid workspaceId, Guid conversationId, CancellationToken ct) =>
        await db.Conversations.FirstOrDefaultAsync(c =>
            c.Id == conversationId && c.WorkspaceId == workspaceId && c.UserId == currentUser.UserId, ct)
        ?? throw new NotFoundException("Conversation", conversationId);
}

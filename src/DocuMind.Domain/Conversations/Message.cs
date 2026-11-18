using DocuMind.Domain.Common;

namespace DocuMind.Domain.Conversations;

public enum MessageRole
{
    User,
    Assistant,
}

public sealed class Message : Entity
{
    private Message() { }

    public Guid ConversationId { get; private init; }
    public MessageRole Role { get; private init; }
    public string Content { get; private init; } = string.Empty;
    public List<Citation> Citations { get; private init; } = [];
    public List<AgentStep> AgentSteps { get; private init; } = [];
    public MessageUsage? Usage { get; private init; }
    public int? Feedback { get; private set; }
    public string? FeedbackComment { get; private set; }

    internal static Message User(Guid conversationId, string content) => new()
    {
        ConversationId = conversationId,
        Role = MessageRole.User,
        Content = content,
    };

    internal static Message Assistant(Guid conversationId, string content, IReadOnlyList<Citation> citations,
        IReadOnlyList<AgentStep> steps, MessageUsage usage) => new()
    {
        ConversationId = conversationId,
        Role = MessageRole.Assistant,
        Content = content,
        Citations = [.. citations],
        AgentSteps = [.. steps],
        Usage = usage,
    };

    /// <param name="score">+1 (helpful) or -1 (not helpful).</param>
    /// <param name="comment">Optional free-text explanation.</param>
    public void GiveFeedback(int score, string? comment)
    {
        if (Role != MessageRole.Assistant)
            throw new DomainException("Feedback can only be given to assistant messages.");
        if (score is not (1 or -1))
            throw new DomainException("Feedback score must be 1 or -1.");
        Feedback = score;
        FeedbackComment = comment;
    }
}

public sealed record Citation(int Number, Guid ChunkId, Guid DocumentId, string FileName, int? Page, string? Section,
    string Snippet, double Score);

public sealed record AgentStep(string Kind, string Name, string? Input, string? Output);

public sealed record MessageUsage(string Model, int InputTokens, int OutputTokens, decimal EstimatedCostUsd,
    long LatencyMs, int RetrievedChunks);

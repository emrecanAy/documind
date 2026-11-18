using DocuMind.Domain.Common;

namespace DocuMind.Domain.Conversations;

public sealed class Conversation : Entity
{
    private readonly List<Message> _messages = [];

    private Conversation() { }

    public Guid WorkspaceId { get; private init; }
    public Guid UserId { get; private init; }
    public string Title { get; private set; } = "New conversation";
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public IReadOnlyCollection<Message> Messages => _messages;

    public static Conversation Start(Guid workspaceId, Guid userId, string firstQuestion) => new()
    {
        WorkspaceId = workspaceId,
        UserId = userId,
        Title = firstQuestion.Length > 80 ? firstQuestion[..77] + "..." : firstQuestion,
    };

    public Message AddUserMessage(string content)
    {
        var message = Message.User(Id, content);
        _messages.Add(message);
        UpdatedAt = DateTimeOffset.UtcNow;
        return message;
    }

    public Message AddAssistantMessage(string content, IReadOnlyList<Citation> citations,
        IReadOnlyList<AgentStep> steps, MessageUsage usage)
    {
        var message = Message.Assistant(Id, content, citations, steps, usage);
        _messages.Add(message);
        UpdatedAt = DateTimeOffset.UtcNow;
        return message;
    }
}

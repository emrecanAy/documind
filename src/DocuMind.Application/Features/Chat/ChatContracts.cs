using System.Text.Json.Serialization;
using DocuMind.Domain.Conversations;
using FluentValidation;

namespace DocuMind.Application.Features.Chat;

public enum ChatMode
{
    Rag,
    Agent,
}

public sealed record AskRequest(string Question, Guid? ConversationId, ChatMode Mode = ChatMode.Rag);

public sealed class AskRequestValidator : AbstractValidator<AskRequest>
{
    public AskRequestValidator()
    {
        RuleFor(x => x.Question).NotEmpty().MaximumLength(4000);
        RuleFor(x => x.Mode).IsInEnum();
    }
}

/// <summary>Events streamed to the client over Server-Sent Events. <see cref="Type"/> becomes the SSE event name.</summary>
public abstract record ChatEvent
{
    [JsonIgnore]
    public abstract string Type { get; }
}

public sealed record ConversationStarted(Guid ConversationId, string Title) : ChatEvent
{
    public override string Type => "conversation";
}

public sealed record StatusUpdate(string Stage, string Message) : ChatEvent
{
    public override string Type => "status";
}

public sealed record SourcesFound(IReadOnlyList<Citation> Citations) : ChatEvent
{
    public override string Type => "sources";
}

public sealed record TextDelta(string Text) : ChatEvent
{
    public override string Type => "delta";
}

public sealed record AgentStepEvent(AgentStep Step) : ChatEvent
{
    public override string Type => "step";
}

public sealed record AnswerCompleted(Guid MessageId, MessageUsage Usage) : ChatEvent
{
    public override string Type => "done";
}

public sealed record ChatError(string Message) : ChatEvent
{
    public override string Type => "error";
}

public sealed record ConversationSummary(Guid Id, string Title, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record MessageDto(Guid Id, MessageRole Role, string Content, IReadOnlyList<Citation> Citations,
    IReadOnlyList<AgentStep> AgentSteps, MessageUsage? Usage, int? Feedback, DateTimeOffset CreatedAt);

public sealed record ConversationDetails(Guid Id, string Title, IReadOnlyList<MessageDto> Messages);

public sealed record FeedbackRequest(int Score, string? Comment);

public sealed class FeedbackRequestValidator : AbstractValidator<FeedbackRequest>
{
    public FeedbackRequestValidator()
    {
        RuleFor(x => x.Score).Must(s => s is 1 or -1).WithMessage("Score must be 1 or -1.");
        RuleFor(x => x.Comment).MaximumLength(1000);
    }
}

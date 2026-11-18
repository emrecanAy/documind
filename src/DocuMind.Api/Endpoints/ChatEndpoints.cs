using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using DocuMind.Api.Infrastructure;
using DocuMind.Application.Features.Chat;

namespace DocuMind.Api.Endpoints;

internal static class ChatEndpoints
{
    public static IEndpointRouteBuilder MapChatEndpoints(this IEndpointRouteBuilder app)
    {
        var workspace = app.MapGroup("/api/workspaces/{workspaceId:guid}").RequireAuthorization();

        // Streaming answer over Server-Sent Events: conversation → status* → sources → delta* → done
        workspace.MapPost("/chat", async (Guid workspaceId, AskRequest request, ChatService chat, ILoggerFactory loggers,
                CancellationToken ct) =>
            {
                // Authorization, validation and persistence of the question happen before the stream starts,
                // so failures still produce proper HTTP status codes.
                var session = await chat.StartAsync(workspaceId, request, ct);
                return TypedResults.ServerSentEvents(Stream(chat.StreamAsync(session, ct), loggers.CreateLogger("Chat"), ct));
            })
            .Validate<AskRequest>()
            .RequireRateLimiting(RateLimitPolicies.Chat)
            .WithTags("Chat");

        var conversations = workspace.MapGroup("/conversations").WithTags("Conversations");

        conversations.MapGet("/", async (Guid workspaceId, ConversationService service, CancellationToken ct) =>
            TypedResults.Ok(await service.ListAsync(workspaceId, ct)));

        conversations.MapGet("/{conversationId:guid}", async (Guid workspaceId, Guid conversationId,
            ConversationService service, CancellationToken ct) =>
            TypedResults.Ok(await service.GetAsync(workspaceId, conversationId, ct)));

        conversations.MapDelete("/{conversationId:guid}", async (Guid workspaceId, Guid conversationId,
            ConversationService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(workspaceId, conversationId, ct);
            return TypedResults.NoContent();
        });

        conversations.MapPost("/{conversationId:guid}/messages/{messageId:guid}/feedback", async (Guid workspaceId,
                Guid conversationId, Guid messageId, FeedbackRequest request, ConversationService service, CancellationToken ct) =>
            {
                await service.GiveFeedbackAsync(workspaceId, conversationId, messageId, request, ct);
                return TypedResults.NoContent();
            })
            .Validate<FeedbackRequest>();

        return app;
    }

    /// <summary>Maps chat events to SSE items and turns mid-stream failures into an "error" event.</summary>
    private static async IAsyncEnumerable<SseItem<object>> Stream(IAsyncEnumerable<ChatEvent> events, ILogger logger,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await using var enumerator = events.GetAsyncEnumerator(ct);
        while (true)
        {
            ChatEvent current;
            try
            {
                if (!await enumerator.MoveNextAsync())
                    yield break;
                current = enumerator.Current;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Chat stream failed");
                current = new ChatError("The assistant failed to answer. Check that the AI provider is reachable and try again.");
            }

            yield return new SseItem<object>(current, current.Type);
            if (current is ChatError)
                yield break;
        }
    }
}

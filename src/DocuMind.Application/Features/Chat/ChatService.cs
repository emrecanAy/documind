using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using DocuMind.Application.Abstractions;
using DocuMind.Application.Common;
using DocuMind.Application.Features.Retrieval;
using DocuMind.Application.Features.Workspaces;
using DocuMind.Domain.Conversations;
using DocuMind.Domain.Workspaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace DocuMind.Application.Features.Chat;

/// <summary>Everything needed to stream an answer; created (and authorized) before the HTTP response starts.</summary>
public sealed class ChatSession
{
    internal ChatSession(Conversation conversation, bool isNew, RagSettings settings, IReadOnlyList<Message> history,
        string question, ChatMode mode)
    {
        Conversation = conversation;
        IsNew = isNew;
        Settings = settings;
        History = history;
        Question = question;
        Mode = mode;
    }

    public Conversation Conversation { get; }
    public bool IsNew { get; }
    public RagSettings Settings { get; }
    public IReadOnlyList<Message> History { get; }
    public string Question { get; }
    public ChatMode Mode { get; }
}

public sealed class ChatService(
    IAppDbContext db,
    ICurrentUser currentUser,
    IWorkspaceAuthorizer authorizer,
    IRetriever retriever,
    IQueryTransformer queryTransformer,
    IChatClient chatClient,
    IOptions<AiOptions> aiOptions,
    CostCalculator costCalculator)
{
    private const int HistoryMessages = 6;

    public async Task<ChatSession> StartAsync(Guid workspaceId, AskRequest request, CancellationToken ct)
    {
        await authorizer.RequireAsync(workspaceId, WorkspaceRole.Viewer, ct);
        var settings = await db.Workspaces.Where(w => w.Id == workspaceId).Select(w => w.Settings).SingleAsync(ct);

        Conversation conversation;
        IReadOnlyList<Message> history = [];
        var isNew = request.ConversationId is null;
        if (isNew)
        {
            conversation = Conversation.Start(workspaceId, currentUser.UserId, request.Question);
            db.Conversations.Add(conversation);
        }
        else
        {
            conversation = await db.Conversations.FirstOrDefaultAsync(c =>
                               c.Id == request.ConversationId && c.WorkspaceId == workspaceId && c.UserId == currentUser.UserId, ct)
                           ?? throw new NotFoundException("Conversation", request.ConversationId!);
            history = (await db.Messages
                    .Where(m => m.ConversationId == conversation.Id)
                    .OrderByDescending(m => m.CreatedAt)
                    .Take(HistoryMessages)
                    .ToListAsync(ct))
                .OrderBy(m => m.CreatedAt)
                .ToList();
        }

        conversation.AddUserMessage(request.Question);
        await db.SaveChangesAsync(ct);
        return new ChatSession(conversation, isNew, settings, history, request.Question, request.Mode);
    }

    public async IAsyncEnumerable<ChatEvent> StreamAsync(ChatSession session, [EnumeratorCancellation] CancellationToken ct)
    {
        using var activity = DocuMindTelemetry.ActivitySource.StartActivity($"chat.{session.Mode.ToString().ToLowerInvariant()}");
        activity?.SetTag("documind.conversation_id", session.Conversation.Id);
        var stopwatch = Stopwatch.StartNew();
        var turn = new Turn();

        if (session.IsNew)
            yield return new ConversationStarted(session.Conversation.Id, session.Conversation.Title);

        var events = session.Mode == ChatMode.Agent ? AgentAsync(session, turn, ct) : RagAsync(session, turn, ct);
        await foreach (var evt in events)
            yield return evt;

        stopwatch.Stop();
        var answer = turn.Answer.ToString().Trim();
        var model = turn.Model ?? aiOptions.Value.ChatModel;

        // Not every provider reports usage while streaming; fall back to a tokenizer estimate.
        var inputTokens = turn.InputTokens > 0 ? turn.InputTokens : turn.PromptTokensEstimate;
        var outputTokens = turn.OutputTokens > 0 ? turn.OutputTokens : TokenCounter.Count(answer);
        var cost = costCalculator.Estimate(model, inputTokens, outputTokens);

        var usage = new MessageUsage(model, inputTokens, outputTokens, cost, stopwatch.ElapsedMilliseconds, turn.Citations.Count);
        var message = session.Conversation.AddAssistantMessage(answer, turn.Citations, turn.Steps, usage);
        await db.SaveChangesAsync(CancellationToken.None);

        DocuMindTelemetry.TokensUsed.Add(inputTokens, new KeyValuePair<string, object?>("direction", "input"), new("model", model));
        DocuMindTelemetry.TokensUsed.Add(outputTokens, new KeyValuePair<string, object?>("direction", "output"), new("model", model));
        DocuMindTelemetry.EstimatedCost.Add((double)cost, new KeyValuePair<string, object?>("model", model));

        yield return new AnswerCompleted(message.Id, usage);
    }

    private async IAsyncEnumerable<ChatEvent> RagAsync(ChatSession session, Turn turn, [EnumeratorCancellation] CancellationToken ct)
    {
        var query = session.Question;
        if (session.Settings.UseQueryRewriting && session.History.Count > 0)
        {
            yield return new StatusUpdate("rewriting", "Understanding the question in context…");
            query = await queryTransformer.RewriteAsync(session.Question, session.History, ct);
            if (query != session.Question)
                turn.Steps.Add(new AgentStep("rewrite", "query_rewrite", session.Question, query));
        }

        yield return new StatusUpdate("retrieving", "Searching documents…");
        var retrieval = await retriever.RetrieveAsync(new RetrievalRequest(session.Conversation.WorkspaceId, query, session.Settings), ct);
        if (retrieval.Queries.Count > 1)
            turn.Steps.Add(new AgentStep("expand", "query_expansion", query, string.Join(" | ", retrieval.Queries.Skip(1))));
        turn.Steps.Add(new AgentStep("retrieve", "hybrid_search", query, $"{retrieval.Chunks.Count} passages in {retrieval.ElapsedMs} ms"));

        var context = ContextBuilder.Build(retrieval.Chunks, aiOptions.Value.ContextTokenBudget);
        turn.Citations.AddRange(context.Citations);
        yield return new SourcesFound(context.Citations);

        List<ChatMessage> messages = [new(ChatRole.System, Prompts.GroundedSystem)];
        messages.AddRange(ToChatMessages(session.History));
        messages.Add(new ChatMessage(ChatRole.User, Prompts.BuildGroundedUserPrompt(session.Question, context.Text)));
        turn.PromptTokensEstimate = messages.Sum(m => TokenCounter.Count(m.Text));

        yield return new StatusUpdate("generating", "Writing the answer…");
        await foreach (var update in chatClient.GetStreamingResponseAsync(messages, new ChatOptions { Temperature = 0.1f }, ct))
        {
            turn.Track(update);
            if (!string.IsNullOrEmpty(update.Text))
            {
                turn.Answer.Append(update.Text);
                yield return new TextDelta(update.Text);
            }
        }
    }

    private async IAsyncEnumerable<ChatEvent> AgentAsync(ChatSession session, Turn turn, [EnumeratorCancellation] CancellationToken ct)
    {
        var toolset = new AgentToolset(db, retriever, session.Conversation.WorkspaceId, session.Settings);
        var options = new ChatOptions
        {
            Temperature = 0.1f,
            Tools = toolset.CreateTools(),
            ToolMode = ChatToolMode.Auto,
        };

        List<ChatMessage> messages = [new(ChatRole.System, Prompts.AgentSystem)];
        messages.AddRange(ToChatMessages(session.History));
        messages.Add(new ChatMessage(ChatRole.User, session.Question));
        turn.PromptTokensEstimate = messages.Sum(m => TokenCounter.Count(m.Text));

        yield return new StatusUpdate("thinking", "Planning which documents to consult…");
        var calls = new Dictionary<string, FunctionCallContent>();
        await foreach (var update in chatClient.GetStreamingResponseAsync(messages, options, ct))
        {
            turn.Track(update);
            foreach (var content in update.Contents)
            {
                switch (content)
                {
                    case FunctionCallContent call:
                        calls[call.CallId] = call;
                        var callStep = new AgentStep("tool_call", call.Name, JsonSerializer.Serialize(call.Arguments), null);
                        turn.Steps.Add(callStep);
                        yield return new AgentStepEvent(callStep);
                        break;
                    case FunctionResultContent result:
                        var name = calls.TryGetValue(result.CallId, out var c) ? c.Name : "tool";
                        var output = result.Result?.ToString() ?? string.Empty;
                        var resultStep = new AgentStep("tool_result", name, null, output.Length > 600 ? output[..600] + "…" : output);
                        turn.Steps.Add(resultStep);
                        yield return new AgentStepEvent(resultStep);
                        break;
                    case TextContent text when update.Role != ChatRole.Tool && !string.IsNullOrEmpty(text.Text):
                        turn.Answer.Append(text.Text);
                        yield return new TextDelta(text.Text);
                        break;
                }
            }
        }

        // Only surface sources the model actually cited, falling back to everything it looked at.
        var answer = turn.Answer.ToString();
        var cited = toolset.Citations.Where(c => answer.Contains($"[{c.Number}]", StringComparison.Ordinal)).ToList();
        turn.Citations.AddRange(cited.Count > 0 ? cited : toolset.Citations);
        yield return new SourcesFound(turn.Citations);
    }

    private static IEnumerable<ChatMessage> ToChatMessages(IReadOnlyList<Message> history) =>
        history.Select(m => new ChatMessage(m.Role == MessageRole.User ? ChatRole.User : ChatRole.Assistant, m.Content));

    private sealed class Turn
    {
        public StringBuilder Answer { get; } = new();
        public List<Citation> Citations { get; } = [];
        public List<AgentStep> Steps { get; } = [];
        public string? Model { get; private set; }
        public int InputTokens { get; private set; }
        public int OutputTokens { get; private set; }
        public int PromptTokensEstimate { get; set; }

        public void Track(ChatResponseUpdate update)
        {
            Model ??= update.ModelId;
            foreach (var usage in update.Contents.OfType<UsageContent>())
            {
                InputTokens += (int)(usage.Details.InputTokenCount ?? 0);
                OutputTokens += (int)(usage.Details.OutputTokenCount ?? 0);
            }
        }
    }
}

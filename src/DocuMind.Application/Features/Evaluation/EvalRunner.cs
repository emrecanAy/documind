using System.Diagnostics;
using System.Text.Json;
using DocuMind.Application.Abstractions;
using DocuMind.Application.Common;
using DocuMind.Application.Features.Chat;
using DocuMind.Application.Features.Retrieval;
using DocuMind.Domain.Evaluation;
using DocuMind.Domain.Workspaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Quality;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocuMind.Application.Features.Evaluation;

/// <summary>
/// Runs a golden dataset through the exact production RAG pipeline and scores it:
/// retrieval quality (hit rate, recall, MRR) deterministically, and answer quality (relevance, groundedness,
/// coherence on a 1-5 scale) with Microsoft.Extensions.AI.Evaluation's LLM-as-a-judge evaluators.
/// </summary>
public sealed class EvalRunner(
    IAppDbContext db,
    IRetriever retriever,
    IChatClient chatClient,
    IOptions<AiOptions> aiOptions,
    ILogger<EvalRunner> logger)
{
    public async Task RunAsync(Guid runId, CancellationToken ct)
    {
        var run = await db.EvalRuns.FirstOrDefaultAsync(r => r.Id == runId, ct);
        if (run is null || run.Status != EvalRunStatus.Pending)
            return;

        using var activity = DocuMindTelemetry.ActivitySource.StartActivity("evaluation.run");
        activity?.SetTag("documind.eval_run_id", runId);

        run.Start();
        await db.SaveChangesAsync(ct);

        try
        {
            var dataset = JsonSerializer.Deserialize<EvalDataset>(run.DatasetJson)
                          ?? throw new InvalidOperationException("Dataset could not be read.");
            var settings = await db.Workspaces.Where(w => w.Id == run.WorkspaceId).Select(w => w.Settings).SingleAsync(ct);

            var results = new List<EvalItemResult>();
            var totalTokens = 0;
            foreach (var item in dataset.Items)
            {
                var (result, tokens) = await EvaluateItemAsync(run.WorkspaceId, settings, item, run.UseLlmJudge, ct);
                results.Add(result);
                totalTokens += tokens;
            }

            run.Complete(Aggregate(results, totalTokens), results);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Evaluation run {RunId} failed", runId);
            run.Fail(ex.Message);
        }

        await db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task<(EvalItemResult Result, int Tokens)> EvaluateItemAsync(Guid workspaceId, RagSettings settings,
        EvalItem item, bool useJudge, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var retrieval = await retriever.RetrieveAsync(new RetrievalRequest(workspaceId, item.Question, settings), ct);
            var retrievedDocs = retrieval.Chunks.Select(c => c.FileName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            var context = ContextBuilder.Build(retrieval.Chunks, aiOptions.Value.ContextTokenBudget);
            List<ChatMessage> messages =
            [
                new(ChatRole.System, Prompts.GroundedSystem),
                new(ChatRole.User, Prompts.BuildGroundedUserPrompt(item.Question, context.Text)),
            ];
            var response = await chatClient.GetResponseAsync(messages, new ChatOptions { Temperature = 0 }, ct);
            stopwatch.Stop();

            double? relevance = null, groundedness = null, coherence = null;
            if (useJudge)
                (relevance, groundedness, coherence) = await JudgeAsync(item.Question, response, context.Text, ct);

            var tokens = (int)((response.Usage?.InputTokenCount ?? 0) + (response.Usage?.OutputTokenCount ?? 0));
            return (new EvalItemResult(
                item.Question,
                item.ExpectedDocuments,
                retrievedDocs,
                RetrievalMetrics.Hit(item.ExpectedDocuments, retrievedDocs),
                RetrievalMetrics.Recall(item.ExpectedDocuments, retrievedDocs),
                RetrievalMetrics.ReciprocalRank(item.ExpectedDocuments, retrievedDocs),
                response.Text,
                relevance,
                groundedness,
                coherence,
                stopwatch.ElapsedMilliseconds,
                null), tokens);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Evaluation item failed: {Question}", item.Question);
            return (new EvalItemResult(item.Question, item.ExpectedDocuments, [], false, 0, 0, string.Empty, null, null,
                null, stopwatch.ElapsedMilliseconds, ex.Message), 0);
        }
    }

    private async Task<(double? Relevance, double? Groundedness, double? Coherence)> JudgeAsync(string question,
        ChatResponse response, string groundingContext, CancellationToken ct)
    {
        var configuration = new ChatConfiguration(chatClient);
        List<ChatMessage> conversation = [new(ChatRole.User, question)];

        var relevance = await ScoreAsync(new RelevanceEvaluator(), RelevanceEvaluator.RelevanceMetricName, null);
        var groundedness = await ScoreAsync(new GroundednessEvaluator(), GroundednessEvaluator.GroundednessMetricName,
            [new GroundednessEvaluatorContext(groundingContext)]);
        var coherence = await ScoreAsync(new CoherenceEvaluator(), CoherenceEvaluator.CoherenceMetricName, null);
        return (relevance, groundedness, coherence);

        async Task<double?> ScoreAsync(IEvaluator evaluator, string metricName, IEnumerable<EvaluationContext>? context)
        {
            try
            {
                var result = await evaluator.EvaluateAsync(conversation, response, configuration, context, ct);
                return result.TryGet<NumericMetric>(metricName, out var metric) ? metric.Value : null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Small local models sometimes fail to follow the judge format; keep the run going.
                logger.LogWarning(ex, "Evaluator {Metric} failed", metricName);
                return null;
            }
        }
    }

    internal static EvalMetrics Aggregate(IReadOnlyList<EvalItemResult> results, int totalTokens)
    {
        static double? AverageOf(IEnumerable<double?> values)
        {
            var present = values.Where(v => v.HasValue).Select(v => v!.Value).ToList();
            return present.Count == 0 ? null : Math.Round(present.Average(), 3);
        }

        return new EvalMetrics(
            results.Count,
            Math.Round(results.Average(r => r.Hit ? 1.0 : 0.0), 3),
            Math.Round(results.Average(r => r.Recall), 3),
            Math.Round(results.Average(r => r.ReciprocalRank), 3),
            AverageOf(results.Select(r => r.Relevance)),
            AverageOf(results.Select(r => r.Groundedness)),
            AverageOf(results.Select(r => r.Coherence)),
            Math.Round(results.Average(r => r.LatencyMs), 1),
            totalTokens);
    }
}

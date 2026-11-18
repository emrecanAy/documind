using DocuMind.Domain.Evaluation;
using FluentValidation;

namespace DocuMind.Application.Features.Evaluation;

/// <summary>A golden dataset: questions with the documents that should be retrieved and (optionally) a reference answer.</summary>
public sealed record EvalDataset(string Name, IReadOnlyList<EvalItem> Items);

public sealed record EvalItem(string Question, IReadOnlyList<string> ExpectedDocuments, string? ReferenceAnswer = null);

public sealed record StartEvalRequest(EvalDataset Dataset, bool UseLlmJudge = true);

public sealed record EvalRunSummary(Guid Id, string DatasetName, EvalRunStatus Status, bool UseLlmJudge,
    EvalMetrics? Metrics, string SettingsSnapshot, string? Error, DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt);

public sealed record EvalRunDetails(EvalRunSummary Run, IReadOnlyList<EvalItemResult> Results);

public sealed class StartEvalRequestValidator : AbstractValidator<StartEvalRequest>
{
    public StartEvalRequestValidator()
    {
        RuleFor(x => x.Dataset).NotNull();
        RuleFor(x => x.Dataset.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Dataset.Items).NotEmpty().Must(i => i.Count <= 200).WithMessage("At most 200 items per run.");
        RuleForEach(x => x.Dataset.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.Question).NotEmpty().MaximumLength(2000);
            item.RuleFor(i => i.ExpectedDocuments).NotNull();
        });
    }
}

public interface IEvalQueue
{
    ValueTask EnqueueAsync(Guid runId, CancellationToken ct);
    IAsyncEnumerable<Guid> DequeueAllAsync(CancellationToken ct);
}

public static class RetrievalMetrics
{
    /// <summary>Did at least one expected document appear in the results?</summary>
    public static bool Hit(IReadOnlyList<string> expected, IReadOnlyList<string> retrieved) =>
        expected.Count == 0 || retrieved.Any(r => expected.Contains(r, StringComparer.OrdinalIgnoreCase));

    /// <summary>Fraction of expected documents that were retrieved.</summary>
    public static double Recall(IReadOnlyList<string> expected, IReadOnlyList<string> retrieved) =>
        expected.Count == 0
            ? 1
            : (double)expected.Count(e => retrieved.Contains(e, StringComparer.OrdinalIgnoreCase)) / expected.Count;

    /// <summary>1 / rank of the first relevant document (0 when none was retrieved).</summary>
    public static double ReciprocalRank(IReadOnlyList<string> expected, IReadOnlyList<string> retrieved)
    {
        if (expected.Count == 0)
            return 1;
        for (var i = 0; i < retrieved.Count; i++)
        {
            if (expected.Contains(retrieved[i], StringComparer.OrdinalIgnoreCase))
                return 1.0 / (i + 1);
        }

        return 0;
    }
}

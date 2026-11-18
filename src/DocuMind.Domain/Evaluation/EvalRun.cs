using DocuMind.Domain.Common;

namespace DocuMind.Domain.Evaluation;

public enum EvalRunStatus
{
    Pending,
    Running,
    Completed,
    Failed,
}

public sealed class EvalRun : Entity
{
    private EvalRun() { }

    public Guid WorkspaceId { get; private init; }
    public Guid StartedBy { get; private init; }
    public string DatasetName { get; private init; } = string.Empty;
    public string DatasetJson { get; private init; } = string.Empty;
    public bool UseLlmJudge { get; private init; }
    public string SettingsSnapshot { get; private init; } = string.Empty;
    public EvalRunStatus Status { get; private set; }
    public EvalMetrics? Metrics { get; private set; }
    public List<EvalItemResult> Results { get; private set; } = [];
    public string? Error { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    public static EvalRun Create(Guid workspaceId, Guid startedBy, string datasetName, string datasetJson,
        bool useLlmJudge, string settingsSnapshot) => new()
    {
        WorkspaceId = workspaceId,
        StartedBy = startedBy,
        DatasetName = datasetName,
        DatasetJson = datasetJson,
        UseLlmJudge = useLlmJudge,
        SettingsSnapshot = settingsSnapshot,
        Status = EvalRunStatus.Pending,
    };

    public void Start() => Status = EvalRunStatus.Running;

    public void Complete(EvalMetrics metrics, List<EvalItemResult> results)
    {
        Status = EvalRunStatus.Completed;
        Metrics = metrics;
        Results = results;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    public void Fail(string error)
    {
        Status = EvalRunStatus.Failed;
        Error = error;
        CompletedAt = DateTimeOffset.UtcNow;
    }
}

public sealed record EvalMetrics(int Items, double HitRate, double Recall, double Mrr, double? Relevance,
    double? Groundedness, double? Coherence, double AvgLatencyMs, int TotalTokens);

public sealed record EvalItemResult(string Question, IReadOnlyList<string> ExpectedDocuments,
    IReadOnlyList<string> RetrievedDocuments, bool Hit, double Recall, double ReciprocalRank, string Answer,
    double? Relevance, double? Groundedness, double? Coherence, long LatencyMs, string? Error);

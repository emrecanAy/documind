using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace DocuMind.Application.Common;

/// <summary>Custom traces and metrics for the RAG pipeline (exported via OpenTelemetry).</summary>
public static class DocuMindTelemetry
{
    public const string SourceName = "DocuMind";

    public static readonly ActivitySource ActivitySource = new(SourceName);
    private static readonly Meter Meter = new(SourceName);

    public static readonly Counter<long> DocumentsIngested =
        Meter.CreateCounter<long>("documind.ingestion.documents", description: "Documents processed by the ingestion pipeline");

    public static readonly Counter<long> ChunksIndexed =
        Meter.CreateCounter<long>("documind.ingestion.chunks", description: "Chunks written to the vector store");

    public static readonly Histogram<double> RetrievalDuration =
        Meter.CreateHistogram<double>("documind.retrieval.duration", unit: "ms", description: "End-to-end retrieval latency");

    public static readonly Counter<long> TokensUsed =
        Meter.CreateCounter<long>("documind.llm.tokens", description: "LLM tokens consumed (tag: direction=input|output)");

    public static readonly Counter<double> EstimatedCost =
        Meter.CreateCounter<double>("documind.llm.cost", unit: "USD", description: "Estimated LLM spend");

    public static readonly Counter<long> Feedback =
        Meter.CreateCounter<long>("documind.feedback", description: "User feedback on answers (tag: score)");
}

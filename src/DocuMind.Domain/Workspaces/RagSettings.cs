using DocuMind.Domain.Common;

namespace DocuMind.Domain.Workspaces;

public enum ChunkingStrategy
{
    FixedToken,
    Recursive,
    Semantic,
}

/// <summary>Per-workspace tuning knobs for the ingestion and retrieval pipeline.</summary>
public sealed record RagSettings
{
    public ChunkingStrategy ChunkingStrategy { get; init; } = ChunkingStrategy.Recursive;
    public int ChunkSize { get; init; } = 512;
    public int ChunkOverlap { get; init; } = 64;
    public int TopK { get; init; } = 6;
    public bool UseHybridSearch { get; init; } = true;
    public bool UseReranker { get; init; }
    public bool UseQueryRewriting { get; init; } = true;
    public bool UseQueryExpansion { get; init; }
    public double SemanticBreakpointPercentile { get; init; } = 0.9;

    public static RagSettings Default => new();

    public void Validate()
    {
        if (ChunkSize is < 64 or > 4096)
            throw new DomainException("Chunk size must be between 64 and 4096 tokens.");
        if (ChunkOverlap < 0 || ChunkOverlap >= ChunkSize / 2)
            throw new DomainException("Chunk overlap must be >= 0 and less than half of the chunk size.");
        if (TopK is < 1 or > 20)
            throw new DomainException("TopK must be between 1 and 20.");
        if (SemanticBreakpointPercentile is <= 0.5 or >= 1)
            throw new DomainException("Semantic breakpoint percentile must be between 0.5 and 1.");
    }
}

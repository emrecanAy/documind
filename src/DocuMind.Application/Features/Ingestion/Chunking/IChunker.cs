using DocuMind.Domain.Workspaces;

namespace DocuMind.Application.Features.Ingestion.Chunking;

public interface IChunker
{
    ChunkingStrategy Strategy { get; }

    Task<IReadOnlyList<TextChunk>> ChunkAsync(ParsedDocument document, RagSettings settings, CancellationToken ct);
}

public interface IChunkerFactory
{
    IChunker Get(ChunkingStrategy strategy);
}

internal sealed class ChunkerFactory(IEnumerable<IChunker> chunkers) : IChunkerFactory
{
    private readonly Dictionary<ChunkingStrategy, IChunker> _chunkers = chunkers.ToDictionary(c => c.Strategy);

    public IChunker Get(ChunkingStrategy strategy) =>
        _chunkers.TryGetValue(strategy, out var chunker)
            ? chunker
            : throw new InvalidOperationException($"No chunker registered for strategy '{strategy}'.");
}

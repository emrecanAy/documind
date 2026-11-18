using DocuMind.Application.Common;
using DocuMind.Domain.Workspaces;

namespace DocuMind.Application.Features.Ingestion.Chunking;

/// <summary>Baseline strategy: fixed size token windows with overlap. Fast and predictable, ignores semantics.</summary>
public sealed class FixedTokenChunker : IChunker
{
    public ChunkingStrategy Strategy => ChunkingStrategy.FixedToken;

    public Task<IReadOnlyList<TextChunk>> ChunkAsync(ParsedDocument document, RagSettings settings, CancellationToken ct)
    {
        var chunks = new List<TextChunk>();
        foreach (var section in ChunkingHelpers.MergeSmallSections(document.Sections, settings.ChunkSize))
        {
            foreach (var piece in ChunkingHelpers.SplitByTokens(section.Text, settings.ChunkSize, settings.ChunkOverlap))
                chunks.Add(new TextChunk(chunks.Count, piece, section.Page, section.Heading, TokenCounter.Count(piece)));
        }

        return Task.FromResult<IReadOnlyList<TextChunk>>(chunks);
    }
}

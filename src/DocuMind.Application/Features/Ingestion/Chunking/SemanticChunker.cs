using System.Numerics.Tensors;
using DocuMind.Application.Abstractions;
using DocuMind.Application.Common;
using DocuMind.Domain.Workspaces;

namespace DocuMind.Application.Features.Ingestion.Chunking;

/// <summary>
/// Semantic strategy: embeds each sentence (with a one-sentence window on each side) and starts a new chunk
/// where the cosine distance between neighbours exceeds the configured percentile, i.e. where the topic shifts.
/// Highest quality boundaries, but costs one embedding per sentence at ingestion time.
/// </summary>
public sealed class SemanticChunker(IEmbeddingService embeddings) : IChunker
{
    public ChunkingStrategy Strategy => ChunkingStrategy.Semantic;

    public async Task<IReadOnlyList<TextChunk>> ChunkAsync(ParsedDocument document, RagSettings settings, CancellationToken ct)
    {
        var chunks = new List<TextChunk>();
        foreach (var section in ChunkingHelpers.MergeSmallSections(document.Sections, settings.ChunkSize))
        {
            var groups = await SplitSectionAsync(section.Text, settings, ct);
            foreach (var group in groups)
            {
                // a semantic group can still exceed the budget: fall back to recursive splitting
                foreach (var piece in RecursiveChunker.Split(group, settings.ChunkSize, settings.ChunkOverlap))
                    chunks.Add(new TextChunk(chunks.Count, piece, section.Page, section.Heading, TokenCounter.Count(piece)));
            }
        }

        return chunks;
    }

    private async Task<IReadOnlyList<string>> SplitSectionAsync(string text, RagSettings settings, CancellationToken ct)
    {
        var sentences = ChunkingHelpers.SplitSentences(text);
        if (sentences.Count <= 3)
            return [text];

        var windows = sentences
            .Select((_, i) => string.Join(' ', sentences.Skip(Math.Max(0, i - 1)).Take(i == 0 ? 2 : 3)))
            .ToList();
        var vectors = await embeddings.EmbedDocumentsAsync(windows, ct);

        var distances = new double[sentences.Count - 1];
        for (var i = 0; i < distances.Length; i++)
            distances[i] = 1 - TensorPrimitives.CosineSimilarity(vectors[i], vectors[i + 1]);

        var threshold = Percentile(distances, settings.SemanticBreakpointPercentile);

        var groups = new List<string>();
        var current = new List<string> { sentences[0] };
        for (var i = 1; i < sentences.Count; i++)
        {
            if (distances[i - 1] > threshold)
            {
                groups.Add(string.Join(' ', current));
                current = [];
            }

            current.Add(sentences[i]);
        }

        groups.Add(string.Join(' ', current));
        return groups;
    }

    internal static double Percentile(IReadOnlyList<double> values, double percentile)
    {
        var sorted = values.Order().ToArray();
        var rank = percentile * (sorted.Length - 1);
        var lower = (int)Math.Floor(rank);
        var upper = (int)Math.Ceiling(rank);
        return sorted[lower] + ((sorted[upper] - sorted[lower]) * (rank - lower));
    }
}

using DocuMind.Application.Common;
using DocuMind.Domain.Workspaces;

namespace DocuMind.Application.Features.Ingestion.Chunking;

/// <summary>
/// Structure-aware strategy: splits on the coarsest boundary that fits (paragraph, line, sentence, clause, word)
/// and greedily packs pieces up to the chunk size with token overlap. Default and best general-purpose choice.
/// </summary>
public sealed class RecursiveChunker : IChunker
{
    private static readonly string[] Separators = ["\n\n", "\n", ". ", "? ", "! ", "; ", ", ", " "];

    public ChunkingStrategy Strategy => ChunkingStrategy.Recursive;

    public Task<IReadOnlyList<TextChunk>> ChunkAsync(ParsedDocument document, RagSettings settings, CancellationToken ct)
    {
        var chunks = new List<TextChunk>();
        foreach (var section in ChunkingHelpers.MergeSmallSections(document.Sections, settings.ChunkSize))
        {
            foreach (var piece in Split(section.Text, settings.ChunkSize, settings.ChunkOverlap))
                chunks.Add(new TextChunk(chunks.Count, piece, section.Page, section.Heading, TokenCounter.Count(piece)));
        }

        return Task.FromResult<IReadOnlyList<TextChunk>>(chunks);
    }

    internal static IReadOnlyList<string> Split(string text, int size, int overlap)
    {
        var pieces = SplitRecursive(text, size, 0);
        return Merge(pieces, size, overlap);
    }

    private static List<string> SplitRecursive(string text, int size, int separatorIndex)
    {
        if (TokenCounter.Count(text) <= size)
            return [text];

        for (var i = separatorIndex; i < Separators.Length; i++)
        {
            var separator = Separators[i];
            if (!text.Contains(separator, StringComparison.Ordinal))
                continue;

            var result = new List<string>();
            var parts = text.Split(separator);
            for (var p = 0; p < parts.Length; p++)
            {
                // keep the separator attached so merged text reads naturally
                var part = p < parts.Length - 1 ? parts[p] + separator : parts[p];
                if (part.Length == 0)
                    continue;
                if (TokenCounter.Count(part) > size)
                    result.AddRange(SplitRecursive(part, size, i + 1));
                else
                    result.Add(part);
            }

            return result;
        }

        return [.. ChunkingHelpers.SplitByTokens(text, size, 0)];
    }

    private static List<string> Merge(List<string> pieces, int size, int overlap)
    {
        var chunks = new List<string>();
        var window = new List<(string Text, int Tokens)>();
        var windowTokens = 0;

        foreach (var piece in pieces)
        {
            var tokens = TokenCounter.Count(piece);
            if (windowTokens + tokens > size && window.Count > 0)
            {
                Emit();
                // carry over the tail of the previous chunk as overlap
                while (window.Count > 0 && (windowTokens > overlap || windowTokens + tokens > size))
                {
                    windowTokens -= window[0].Tokens;
                    window.RemoveAt(0);
                }
            }

            window.Add((piece, tokens));
            windowTokens += tokens;
        }

        if (window.Count > 0)
            Emit();
        return chunks;

        void Emit()
        {
            var text = string.Concat(window.Select(w => w.Text)).Trim();
            if (text.Length > 0 && (chunks.Count == 0 || chunks[^1] != text))
                chunks.Add(text);
        }
    }
}

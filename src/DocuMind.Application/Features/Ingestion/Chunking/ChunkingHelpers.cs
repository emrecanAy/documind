using System.Text.RegularExpressions;
using DocuMind.Application.Common;

namespace DocuMind.Application.Features.Ingestion.Chunking;

internal static partial class ChunkingHelpers
{
    /// <summary>
    /// Merges adjacent tiny sections (e.g. a heading followed by a one-line paragraph) so we do not
    /// produce low-information chunks. Sections are only merged when they share the same page.
    /// </summary>
    public static IReadOnlyList<ParsedSection> MergeSmallSections(IReadOnlyList<ParsedSection> sections, int maxTokens)
    {
        var minTokens = Math.Max(16, maxTokens / 4);
        var result = new List<ParsedSection>();
        ParsedSection? pending = null;
        var pendingTokens = 0;

        foreach (var section in sections.Where(s => !string.IsNullOrWhiteSpace(s.Text)))
        {
            var tokens = TokenCounter.Count(section.Text);
            if (pending is not null && pending.Page == section.Page && pendingTokens < minTokens &&
                pendingTokens + tokens <= maxTokens)
            {
                // keep the merged section's own heading in the text so "Daily Limits" stays searchable/readable
                var title = section.Heading is not null && section.Heading != pending.Heading
                    ? section.Heading.Split(" > ")[^1] + "\n"
                    : string.Empty;
                pending = pending with { Text = pending.Text + "\n\n" + title + section.Text, Heading = pending.Heading ?? section.Heading };
                pendingTokens += tokens;
                continue;
            }

            if (pending is not null)
                result.Add(pending);
            pending = section;
            pendingTokens = tokens;
        }

        if (pending is not null)
            result.Add(pending);
        return result;
    }

    /// <summary>Hard token window split used as the last resort by every strategy.</summary>
    public static IEnumerable<string> SplitByTokens(string text, int size, int overlap)
    {
        var ids = TokenCounter.Encode(text);
        if (ids.Count <= size)
        {
            yield return text.Trim();
            yield break;
        }

        var step = Math.Max(1, size - overlap);
        for (var start = 0; start < ids.Count; start += step)
        {
            // Decoding a token window and re-encoding it can merge differently at the edges (BPE),
            // so shrink the window until the text really fits the budget.
            var take = Math.Min(size, ids.Count - start);
            var piece = TokenCounter.Decode(ids.Skip(start).Take(take)).Trim();
            while (take > 1 && TokenCounter.Count(piece) > size)
            {
                take--;
                piece = TokenCounter.Decode(ids.Skip(start).Take(take)).Trim();
            }

            if (piece.Length > 0)
                yield return piece;
            if (start + size >= ids.Count)
                break;
        }
    }

    public static IReadOnlyList<string> SplitSentences(string text)
    {
        return SentenceRegex().Split(text)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();
    }

    public static string NormalizeWhitespace(string text) =>
        MultiBlankLineRegex().Replace(text.Replace("\r\n", "\n"), "\n\n").Trim();

    [GeneratedRegex(@"(?<=[\.\!\?])\s+|\n{2,}")]
    private static partial Regex SentenceRegex();

    [GeneratedRegex(@"\n[ \t]*\n(\s*\n)+")]
    private static partial Regex MultiBlankLineRegex();
}

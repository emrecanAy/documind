using System.Text;
using DocuMind.Application.Common;
using DocuMind.Domain.Conversations;

namespace DocuMind.Application.Features.Retrieval;

public sealed record BuiltContext(string Text, IReadOnlyList<Citation> Citations, int Tokens);

/// <summary>Packs retrieved chunks into a numbered context block within a token budget.</summary>
public static class ContextBuilder
{
    public static BuiltContext Build(IReadOnlyList<RetrievedChunk> chunks, int tokenBudget, int startNumber = 1)
    {
        var builder = new StringBuilder();
        var citations = new List<Citation>();
        var used = 0;

        foreach (var chunk in chunks)
        {
            var number = startNumber + citations.Count;
            var block = Format(number, chunk);
            var tokens = TokenCounter.Count(block);
            if (used + tokens > tokenBudget && citations.Count > 0)
                break;

            builder.Append(block);
            used += tokens;
            citations.Add(ToCitation(number, chunk));
        }

        return new BuiltContext(builder.ToString(), citations, used);
    }

    public static string Format(int number, RetrievedChunk chunk)
    {
        var location = new List<string> { chunk.FileName };
        if (chunk.Page is not null)
            location.Add($"page {chunk.Page}");
        if (!string.IsNullOrWhiteSpace(chunk.Section))
            location.Add($"section \"{chunk.Section}\"");
        return $"[{number}] ({string.Join(", ", location)})\n{chunk.Content}\n\n";
    }

    public static Citation ToCitation(int number, RetrievedChunk chunk) =>
        new(number, chunk.ChunkId, chunk.DocumentId, chunk.FileName, chunk.Page, chunk.Section,
            chunk.Content.Length > 400 ? chunk.Content[..400] + "…" : chunk.Content, Math.Round(chunk.Score, 4));
}

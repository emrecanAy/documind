namespace DocuMind.Application.Features.Ingestion;

/// <summary>Normalized output of every parser: logical sections with page / heading provenance.</summary>
public sealed record ParsedDocument(string FileName, IReadOnlyList<ParsedSection> Sections, int PageCount);

public sealed record ParsedSection(string Text, int? Page, string? Heading);

public sealed record TextChunk(int Index, string Content, int? Page, string? Section, int TokenCount);

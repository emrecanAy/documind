using DocuMind.Application.Abstractions;
using DocuMind.Application.Common;
using DocuMind.Application.Features.Ingestion;
using DocuMind.Application.Features.Ingestion.Chunking;
using DocuMind.Domain.Workspaces;
using NSubstitute;

namespace DocuMind.UnitTests.Chunking;

public sealed class ChunkerTests
{
    private static readonly string LongText = string.Join("\n\n", Enumerable.Range(1, 40).Select(i =>
        $"Paragraph {i}. The quarterly security review covers access rights, device encryption and incident response. " +
        $"Managers must confirm item {i} before the deadline and document exceptions in the tracker."));

    private static ParsedDocument Doc(params ParsedSection[] sections) => new("test.md", sections, 0);

    [Fact]
    public async Task FixedToken_respects_chunk_size_and_overlap()
    {
        var settings = RagSettings.Default with { ChunkSize = 100, ChunkOverlap = 20 };

        var chunks = await new FixedTokenChunker().ChunkAsync(Doc(new ParsedSection(LongText, 1, "Intro")), settings, TestContext.Current.CancellationToken);

        chunks.Count.ShouldBeGreaterThan(5);
        chunks.ShouldAllBe(c => c.TokenCount <= 100);
        chunks.Select(c => c.Index).ShouldBe(Enumerable.Range(0, chunks.Count));
        chunks.ShouldAllBe(c => c.Page == 1 && c.Section == "Intro");
    }

    [Fact]
    public async Task Recursive_prefers_paragraph_boundaries()
    {
        var settings = RagSettings.Default with { ChunkSize = 120, ChunkOverlap = 0 };

        var chunks = await new RecursiveChunker().ChunkAsync(Doc(new ParsedSection(LongText, null, null)), settings, TestContext.Current.CancellationToken);

        chunks.ShouldAllBe(c => c.TokenCount <= 120);
        // every chunk should start at the beginning of a paragraph
        chunks.ShouldAllBe(c => c.Content.StartsWith("Paragraph", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Recursive_overlap_repeats_tail_of_previous_chunk()
    {
        var settings = RagSettings.Default with { ChunkSize = 120, ChunkOverlap = 50 };

        var chunks = await new RecursiveChunker().ChunkAsync(Doc(new ParsedSection(LongText, null, null)), settings, TestContext.Current.CancellationToken);

        chunks.Count.ShouldBeGreaterThan(2);
        var overlapping = chunks.Zip(chunks.Skip(1)).Count(pair =>
            pair.First.Content.Split("\n\n").Last().Trim() is var tail && pair.Second.Content.Contains(tail, StringComparison.Ordinal));
        overlapping.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Small_sections_on_same_page_are_merged()
    {
        var settings = RagSettings.Default with { ChunkSize = 256, ChunkOverlap = 0 };
        var document = Doc(
            new ParsedSection("Short intro.", 1, "Intro"),
            new ParsedSection("Another short line.", 1, "Details"),
            new ParsedSection("Different page.", 2, "Other"));

        var chunks = await new RecursiveChunker().ChunkAsync(document, settings, TestContext.Current.CancellationToken);

        chunks.Count.ShouldBe(2);
        chunks[0].Content.ShouldContain("Short intro.");
        chunks[0].Content.ShouldContain("Another short line.");
        chunks[1].Page.ShouldBe(2);
    }

    [Fact]
    public async Task Semantic_splits_where_topic_changes()
    {
        // Fake embeddings: sentences about "leave" vs "security" get orthogonal vectors.
        var embeddings = Substitute.For<IEmbeddingService>();
        embeddings.EmbedDocumentsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<IReadOnlyList<string>>()
                .Select(t => t.Contains("password", StringComparison.Ordinal) ? new[] { 0f, 1f } : new[] { 1f, 0f })
                .ToList());

        var text = "Employees get twenty days of leave. Leave must be requested early. Unused leave carries over. " +
                   "Leave requests go to managers. A password must be long. Every password needs MFA. Never share a password.";
        var settings = RagSettings.Default with { ChunkSize = 512, ChunkOverlap = 0, SemanticBreakpointPercentile = 0.8 };

        var chunks = await new SemanticChunker(embeddings).ChunkAsync(Doc(new ParsedSection(text, null, null)), settings, TestContext.Current.CancellationToken);

        chunks.Count.ShouldBe(2);
        chunks[0].Content.ShouldNotContain("password");
        chunks[1].Content.ShouldContain("password");
    }

    [Fact]
    public void Percentile_interpolates()
    {
        SemanticChunker.Percentile([0, 10], 0.5).ShouldBe(5);
        SemanticChunker.Percentile([1, 2, 3, 4, 5], 0.9).ShouldBe(4.6, 0.0001);
    }

    [Fact]
    public void Token_counter_is_stable()
    {
        TokenCounter.Count("").ShouldBe(0);
        TokenCounter.Count("Hello world").ShouldBe(2);
    }
}

using System.Text;
using DocuMind.Infrastructure.Parsing;

namespace DocuMind.UnitTests.Parsing;

public sealed class ParserTests
{
    private static MemoryStream Utf8(string text) => new(Encoding.UTF8.GetBytes(text));

    [Fact]
    public async Task Markdown_tracks_heading_hierarchy()
    {
        const string markdown = """
            # Handbook
            Intro text.

            ## Leave
            ### Annual
            You get 20 days.

            ## Benefits
            | Item | Value |
            |---|---|
            | Meal | 400 EUR |
            """;

        var doc = await new MarkdownDocumentParser().ParseAsync(Utf8(markdown), "handbook.md", TestContext.Current.CancellationToken);

        doc.Sections.Select(s => s.Heading).ShouldBe(["Handbook", "Handbook > Leave > Annual", "Handbook > Benefits"]);
        doc.Sections[1].Text.ShouldBe("You get 20 days.");
        doc.Sections[2].Text.ShouldContain("| Meal | 400 EUR |");
    }

    [Fact]
    public async Task Html_strips_chrome_and_splits_on_headings()
    {
        const string html = """
            <html><head><title>Policy</title><script>alert(1)</script></head>
            <body><nav>Menu</nav>
            <h1>Travel</h1><p>Book early.</p>
            <ul><li><p>Economy only</p></li></ul>
            <h2>Limits</h2><table><tr><td>Hotel</td><td>180 EUR</td></tr></table>
            <footer>copyright</footer></body></html>
            """;

        var doc = await new HtmlDocumentParser().ParseAsync(Utf8(html), "p.html", TestContext.Current.CancellationToken);

        doc.Sections.Select(s => s.Heading).ShouldBe(["Travel", "Limits"]);
        doc.Sections[0].Text.ShouldContain("Book early.");
        doc.Sections[0].Text.ShouldContain("- Economy only");
        doc.Sections[0].Text.Split("Economy only").Length.ShouldBe(2); // nested <p> not duplicated
        doc.Sections[1].Text.ShouldBe("| Hotel | 180 EUR |");
        string.Concat(doc.Sections.Select(s => s.Text)).ShouldNotContain("Menu");
        string.Concat(doc.Sections.Select(s => s.Text)).ShouldNotContain("alert");
    }

    [Fact]
    public async Task Plain_text_treats_form_feed_as_page_break()
    {
        var doc = await new PlainTextDocumentParser().ParseAsync(Utf8("page one\fpage two"), "a.txt", TestContext.Current.CancellationToken);

        doc.PageCount.ShouldBe(2);
        doc.Sections.Select(s => s.Page).ShouldBe([1, 2]);
    }
}

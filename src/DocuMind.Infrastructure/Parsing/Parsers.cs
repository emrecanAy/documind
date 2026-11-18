using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using DocuMind.Application.Abstractions;
using DocuMind.Application.Features.Ingestion;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Markdig;
using Markdig.Syntax;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace DocuMind.Infrastructure.Parsing;

/// <summary>PDF: one section per page so citations can point at page numbers.</summary>
internal sealed class PdfDocumentParser : IDocumentParser
{
    public IReadOnlyCollection<string> SupportedExtensions { get; } = [".pdf"];

    public async Task<ParsedDocument> ParseAsync(Stream content, string fileName, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct); // PdfPig needs a seekable stream
        buffer.Position = 0;

        using var pdf = PdfDocument.Open(buffer);
        var sections = new List<ParsedSection>();
        foreach (var page in pdf.GetPages())
        {
            ct.ThrowIfCancellationRequested();
            var text = ContentOrderTextExtractor.GetText(page);
            if (string.IsNullOrWhiteSpace(text))
                text = page.Text;
            if (!string.IsNullOrWhiteSpace(text))
                sections.Add(new ParsedSection(text.Trim(), page.Number, null));
        }

        return new ParsedDocument(fileName, sections, pdf.NumberOfPages);
    }
}

/// <summary>DOCX: headings (Heading1..9 / Title styles) start new sections; tables become pipe separated rows.</summary>
internal sealed class DocxDocumentParser : IDocumentParser
{
    public IReadOnlyCollection<string> SupportedExtensions { get; } = [".docx"];

    public async Task<ParsedDocument> ParseAsync(Stream content, string fileName, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        buffer.Position = 0;

        using var doc = WordprocessingDocument.Open(buffer, false);
        var body = doc.MainDocumentPart?.Document?.Body;
        var sections = new List<ParsedSection>();
        if (body is null)
            return new ParsedDocument(fileName, sections, 0);

        string? heading = null;
        var current = new StringBuilder();

        void Flush()
        {
            if (current.Length > 0)
                sections.Add(new ParsedSection(current.ToString().Trim(), null, heading));
            current.Clear();
        }

        foreach (var element in body.ChildElements)
        {
            switch (element)
            {
                case Paragraph paragraph:
                    var text = paragraph.InnerText.Trim();
                    if (text.Length == 0)
                        continue;
                    var style = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? string.Empty;
                    if (style.StartsWith("Heading", StringComparison.OrdinalIgnoreCase) || style.Equals("Title", StringComparison.OrdinalIgnoreCase))
                    {
                        Flush();
                        heading = text;
                    }
                    else
                    {
                        current.AppendLine(text).AppendLine();
                    }

                    break;
                case Table table:
                    foreach (var row in table.Elements<TableRow>())
                        current.AppendLine("| " + string.Join(" | ", row.Elements<TableCell>().Select(c => c.InnerText.Trim())) + " |");
                    current.AppendLine();
                    break;
            }
        }

        Flush();
        _ = int.TryParse(doc.ExtendedFilePropertiesPart?.Properties?.Pages?.Text, out var pages);
        return new ParsedDocument(fileName, sections, pages);
    }
}

/// <summary>Markdown: heading hierarchy ("Benefits &gt; Leave") becomes the section path; tables/code stay as markdown.</summary>
internal sealed class MarkdownDocumentParser : IDocumentParser
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    public IReadOnlyCollection<string> SupportedExtensions { get; } = [".md", ".markdown"];

    public async Task<ParsedDocument> ParseAsync(Stream content, string fileName, CancellationToken ct)
    {
        using var reader = new StreamReader(content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var markdown = (await reader.ReadToEndAsync(ct)).Replace("\r\n", "\n");
        var document = Markdown.Parse(markdown, Pipeline);

        var sections = new List<ParsedSection>();
        var headingPath = new List<(int Level, string Text)>();
        var current = new StringBuilder();

        void Flush()
        {
            if (current.Length > 0)
            {
                var heading = headingPath.Count == 0 ? null : string.Join(" > ", headingPath.Select(h => h.Text));
                sections.Add(new ParsedSection(current.ToString().Trim(), null, heading));
            }

            current.Clear();
        }

        foreach (var block in document)
        {
            if (block is HeadingBlock heading)
            {
                Flush();
                var text = heading.Inline is null
                    ? string.Empty
                    : string.Concat(heading.Inline.Descendants<Markdig.Syntax.Inlines.LiteralInline>().Select(l => l.Content.ToString()));
                headingPath.RemoveAll(h => h.Level >= heading.Level);
                headingPath.Add((heading.Level, text.Trim()));
                continue;
            }

            var raw = markdown.Substring(block.Span.Start, Math.Min(block.Span.Length, markdown.Length - block.Span.Start)).Trim();
            if (raw.Length > 0)
                current.AppendLine(raw).AppendLine();
        }

        Flush();
        return new ParsedDocument(fileName, sections, 0);
    }
}

/// <summary>HTML: strips chrome (nav/script/style/footer) and walks headings and block elements in document order.</summary>
internal sealed class HtmlDocumentParser : IDocumentParser
{
    private const string BlockSelector = "h1,h2,h3,h4,h5,h6,p,li,pre,blockquote,tr";

    public IReadOnlyCollection<string> SupportedExtensions { get; } = [".html", ".htm"];

    public async Task<ParsedDocument> ParseAsync(Stream content, string fileName, CancellationToken ct)
    {
        var document = await new HtmlParser().ParseDocumentAsync(content, ct);
        foreach (var noise in document.QuerySelectorAll("script,style,nav,footer,header,noscript,svg").ToList())
            noise.Remove();

        var sections = new List<ParsedSection>();
        string? heading = document.Title;
        var current = new StringBuilder();

        void Flush()
        {
            if (current.Length > 0)
                sections.Add(new ParsedSection(current.ToString().Trim(), null, heading));
            current.Clear();
        }

        foreach (var element in document.Body?.QuerySelectorAll(BlockSelector) ?? Enumerable.Empty<IElement>())
        {
            // skip blocks nested inside another selected block (e.g. <p> inside <li>) to avoid duplicates
            if (element.ParentElement?.Closest(BlockSelector) is not null)
                continue;

            var text = element.LocalName == "tr"
                ? "| " + string.Join(" | ", element.Children.Select(c => c.TextContent.Trim())) + " |"
                : element.TextContent.Trim();
            if (text.Length == 0)
                continue;

            if (element.LocalName is ['h', >= '1' and <= '6'])
            {
                Flush();
                heading = text;
            }
            else
            {
                current.AppendLine(element.LocalName == "li" ? "- " + text : text).AppendLine();
            }
        }

        Flush();
        if (sections.Count == 0 && !string.IsNullOrWhiteSpace(document.Body?.TextContent))
            sections.Add(new ParsedSection(document.Body!.TextContent.Trim(), null, document.Title));
        return new ParsedDocument(fileName, sections, 0);
    }
}

/// <summary>Plain text: form feeds are treated as page breaks.</summary>
internal sealed class PlainTextDocumentParser : IDocumentParser
{
    public IReadOnlyCollection<string> SupportedExtensions { get; } = [".txt"];

    public async Task<ParsedDocument> ParseAsync(Stream content, string fileName, CancellationToken ct)
    {
        using var reader = new StreamReader(content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var text = (await reader.ReadToEndAsync(ct)).Replace("\r\n", "\n");
        var pages = text.Split('\f');
        var sections = pages
            .Select((page, i) => new ParsedSection(page.Trim(), pages.Length > 1 ? i + 1 : null, null))
            .Where(s => s.Text.Length > 0)
            .ToList();
        return new ParsedDocument(fileName, sections, pages.Length > 1 ? pages.Length : 0);
    }
}

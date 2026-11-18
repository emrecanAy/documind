using Microsoft.ML.Tokenizers;

namespace DocuMind.Application.Common;

/// <summary>
/// Token counting with the cl100k BPE vocabulary. Not exact for every model, but a stable,
/// provider-independent approximation for chunk sizing and context budgeting.
/// </summary>
public static class TokenCounter
{
    private static readonly Lazy<Tokenizer> LazyTokenizer =
        new(() => TiktokenTokenizer.CreateForEncoding("cl100k_base"));

    public static Tokenizer Tokenizer => LazyTokenizer.Value;

    public static int Count(string text) => string.IsNullOrEmpty(text) ? 0 : Tokenizer.CountTokens(text);

    public static IReadOnlyList<int> Encode(string text) => Tokenizer.EncodeToIds(text);

    public static string Decode(IEnumerable<int> ids) => Tokenizer.Decode(ids) ?? string.Empty;
}

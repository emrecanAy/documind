using System.Text;
using System.Text.RegularExpressions;
using DocuMind.Application.Abstractions;

namespace DocuMind.Application.Features.Retrieval;

/// <summary>
/// Client-side BM25 term-frequency encoder for Qdrant sparse vectors. Qdrant applies the IDF part
/// (collection configured with <c>Modifier.Idf</c>), so document vectors carry the saturated TF and
/// query vectors carry 1.0 per term. Tokens are hashed into a 32-bit index space (feature hashing).
/// This is what powers keyword matching (product codes, names, acronyms) next to dense vectors.
/// </summary>
public static partial class Bm25SparseEncoder
{
    private const double K1 = 1.2;
    private const double B = 0.75;
    private const double AverageDocumentLength = 256;

    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        // English
        "a", "an", "and", "are", "as", "at", "be", "by", "for", "from", "has", "have", "in", "is", "it", "its",
        "of", "on", "or", "that", "the", "this", "to", "was", "were", "will", "with", "what", "which", "who", "how",
        "do", "does", "can", "i", "we", "you", "our", "your",
        // Turkish
        "ve", "veya", "ile", "bir", "bu", "şu", "da", "de", "ki", "mi", "mı", "mu", "mü", "için", "gibi", "olan",
        "olarak", "çok", "daha", "en", "ne", "nasıl", "hangi", "nedir", "var", "yok",
    };

    public static SparseVector EncodeDocument(string text)
    {
        var tokens = Tokenize(text);
        var docLength = tokens.Count;
        var frequencies = tokens.GroupBy(HashToken).ToDictionary(g => g.Key, g => g.Count());

        var indices = new uint[frequencies.Count];
        var values = new float[frequencies.Count];
        var i = 0;
        foreach (var (index, tf) in frequencies.OrderBy(kv => kv.Key))
        {
            indices[i] = index;
            values[i] = (float)(tf * (K1 + 1) / (tf + (K1 * (1 - B + (B * docLength / AverageDocumentLength)))));
            i++;
        }

        return new SparseVector(indices, values);
    }

    public static SparseVector EncodeQuery(string text)
    {
        var indices = Tokenize(text).Select(HashToken).Distinct().Order().ToArray();
        return new SparseVector(indices, Enumerable.Repeat(1f, indices.Length).ToArray());
    }

    public static List<string> Tokenize(string text)
    {
        var lowered = text.ToLowerInvariant();
        return TokenRegex().Matches(lowered)
            .Select(m => m.Value)
            .Where(t => t.Length > 1 && !StopWords.Contains(t))
            .ToList();
    }

    /// <summary>FNV-1a 32-bit: stable across processes (unlike string.GetHashCode).</summary>
    private static uint HashToken(string token)
    {
        const uint offset = 2166136261;
        const uint prime = 16777619;
        var hash = offset;
        foreach (var b in Encoding.UTF8.GetBytes(token))
        {
            hash ^= b;
            hash *= prime;
        }

        return hash;
    }

    [GeneratedRegex(@"[\p{L}\p{N}][\p{L}\p{N}\-_.]*[\p{L}\p{N}]|[\p{L}\p{N}]")]
    private static partial Regex TokenRegex();
}

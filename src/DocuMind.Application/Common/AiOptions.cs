namespace DocuMind.Application.Common;

public sealed class AiOptions
{
    public const string SectionName = "AI";

    /// <summary>ollama | openai | anthropic</summary>
    public string ChatProvider { get; set; } = "ollama";

    /// <summary>ollama | openai</summary>
    public string EmbeddingProvider { get; set; } = "ollama";

    public string ChatModel { get; set; } = "llama3.2:3b";
    public string EmbeddingModel { get; set; } = "nomic-embed-text";

    /// <summary>Some embedding models (nomic, e5, bge) expect task prefixes.</summary>
    public string? DocumentPrefix { get; set; } = "search_document: ";
    public string? QueryPrefix { get; set; } = "search_query: ";

    public int EmbeddingBatchSize { get; set; } = 32;

    /// <summary>Maximum tokens of retrieved context packed into the prompt.</summary>
    public int ContextTokenBudget { get; set; } = 3500;

    public ProviderOptions Ollama { get; set; } = new() { Endpoint = "http://localhost:11434" };
    public ProviderOptions OpenAI { get; set; } = new();
    public ProviderOptions Anthropic { get; set; } = new();

    /// <summary>USD per 1M tokens, keyed by model id. Missing models are treated as free (e.g. local Ollama).</summary>
    public Dictionary<string, ModelPrice> Pricing { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ProviderOptions
{
    public string? Endpoint { get; set; }
    public string? ApiKey { get; set; }
}

public sealed class ModelPrice
{
    public decimal InputPerMillion { get; set; }
    public decimal OutputPerMillion { get; set; }
}

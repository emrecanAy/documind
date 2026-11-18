using System.Runtime.CompilerServices;
using System.Text;
using DocuMind.Application.Features.Retrieval;
using Microsoft.Extensions.AI;

namespace DocuMind.IntegrationTests.Infrastructure;

/// <summary>
/// Deterministic "embedding model": hashed bag-of-words, L2 normalized. Texts sharing words are close in cosine
/// space, which is enough to exercise the real Qdrant hybrid search without running an LLM.
/// </summary>
internal sealed class FakeEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    public const int Dimensions = 128;

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        var embeddings = values.Select(v => new Embedding<float>(Embed(v))).ToList();
        return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(embeddings));
    }

    private static float[] Embed(string text)
    {
        var vector = new float[Dimensions];
        foreach (var token in Bm25SparseEncoder.Tokenize(text))
            vector[(int)((uint)StringComparer.Ordinal.GetHashCode(token) % Dimensions)] += 1;
        var norm = MathF.Sqrt(vector.Sum(v => v * v));
        if (norm == 0)
            vector[0] = 1;
        else
            for (var i = 0; i < vector.Length; i++)
                vector[i] /= norm;
        return vector;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}

/// <summary>Scripted chat model: answers by quoting the first source and citing it.</summary>
internal sealed class FakeChatClient : IChatClient
{
    public const string Model = "fake-llm";

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, Answer(messages)))
        {
            ModelId = Model,
            Usage = new UsageDetails { InputTokenCount = 100, OutputTokenCount = 20 },
        };
        return Task.FromResult(response);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var word in Answer(messages).Split(' '))
        {
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, word + " ") { ModelId = Model };
        }

        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            ModelId = Model,
            Contents = [new UsageContent(new UsageDetails { InputTokenCount = 120, OutputTokenCount = 30 })],
        };
    }

    private static string Answer(IEnumerable<ChatMessage> messages)
    {
        var prompt = messages.Last().Text;
        var sourceStart = prompt.IndexOf("[1]", StringComparison.Ordinal);
        if (sourceStart < 0)
            return "The documents do not cover this.";
        var firstLine = prompt[sourceStart..].Split('\n').Skip(1).FirstOrDefault() ?? string.Empty;
        return new StringBuilder("According to the documents: ").Append(firstLine.Trim()).Append(" [1]").ToString();
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}

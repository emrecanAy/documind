using DocuMind.Application.Abstractions;
using DocuMind.Application.Common;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace DocuMind.Infrastructure.AI;

/// <summary>Adds model-specific task prefixes, batching and dimension discovery on top of IEmbeddingGenerator.</summary>
internal sealed class EmbeddingService(IEmbeddingGenerator<string, Embedding<float>> generator, IOptions<AiOptions> options)
    : IEmbeddingService
{
    private readonly SemaphoreSlim _probeLock = new(1, 1);
    private int _dimensions;

    public string ModelId => options.Value.EmbeddingModel;

    public async Task<int> GetDimensionsAsync(CancellationToken ct)
    {
        if (_dimensions > 0)
            return _dimensions;

        await _probeLock.WaitAsync(ct);
        try
        {
            if (_dimensions == 0)
            {
                var probe = await generator.GenerateAsync(["dimension probe"], cancellationToken: ct);
                _dimensions = probe[0].Vector.Length;
            }

            return _dimensions;
        }
        finally
        {
            _probeLock.Release();
        }
    }

    public async Task<float[]> EmbedQueryAsync(string text, CancellationToken ct)
    {
        var result = await generator.GenerateAsync([options.Value.QueryPrefix + text], cancellationToken: ct);
        return Remember(result[0].Vector.ToArray());
    }

    public async Task<IReadOnlyList<float[]>> EmbedDocumentsAsync(IReadOnlyList<string> texts, CancellationToken ct)
    {
        var batchSize = Math.Max(1, options.Value.EmbeddingBatchSize);
        var vectors = new List<float[]>(texts.Count);
        for (var offset = 0; offset < texts.Count; offset += batchSize)
        {
            var batch = texts.Skip(offset).Take(batchSize).Select(t => options.Value.DocumentPrefix + t);
            var result = await generator.GenerateAsync(batch, cancellationToken: ct);
            vectors.AddRange(result.Select(e => Remember(e.Vector.ToArray())));
        }

        return vectors;
    }

    private float[] Remember(float[] vector)
    {
        if (_dimensions == 0)
            _dimensions = vector.Length;
        return vector;
    }
}

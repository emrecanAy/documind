using System.Threading.Channels;

namespace DocuMind.Application.Features.Evaluation;

/// <summary>In-process queue for evaluation runs, consumed by a hosted service in the API.</summary>
internal sealed class ChannelEvalQueue : IEvalQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true });

    public ValueTask EnqueueAsync(Guid runId, CancellationToken ct) => _channel.Writer.WriteAsync(runId, ct);

    public IAsyncEnumerable<Guid> DequeueAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);
}

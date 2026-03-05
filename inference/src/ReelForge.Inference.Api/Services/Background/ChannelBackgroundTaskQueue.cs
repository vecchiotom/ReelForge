using System.Threading.Channels;

namespace ReelForge.Inference.Api.Services.Background;

/// <summary>
/// In-memory channel-based implementation of the background task queue.
/// </summary>
public class ChannelBackgroundTaskQueue<T> : IBackgroundTaskQueue<T>
{
    private readonly Channel<T> _channel;

    public ChannelBackgroundTaskQueue(int capacity = 100)
    {
        BoundedChannelOptions options = new(capacity) { FullMode = BoundedChannelFullMode.Wait };
        _channel = Channel.CreateBounded<T>(options);
    }

    public async ValueTask QueueAsync(T task, CancellationToken ct = default)
    {
        await _channel.Writer.WriteAsync(task, ct);
    }

    public async ValueTask<T> DequeueAsync(CancellationToken ct)
    {
        return await _channel.Reader.ReadAsync(ct);
    }
}

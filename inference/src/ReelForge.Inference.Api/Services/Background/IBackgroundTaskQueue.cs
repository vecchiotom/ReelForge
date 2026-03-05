namespace ReelForge.Inference.Api.Services.Background;

/// <summary>
/// Generic background task queue backed by System.Threading.Channels.
/// </summary>
public interface IBackgroundTaskQueue<T>
{
    ValueTask QueueAsync(T task, CancellationToken ct = default);
    ValueTask<T> DequeueAsync(CancellationToken ct);
}

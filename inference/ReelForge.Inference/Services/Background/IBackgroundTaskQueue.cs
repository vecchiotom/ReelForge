namespace ReelForge.Inference.Services.Background;

/// <summary>
/// Generic background task queue backed by System.Threading.Channels.
/// Designed to be swappable for a Service Bus queue later.
/// </summary>
public interface IBackgroundTaskQueue<T>
{
    /// <summary>Enqueues a task for background processing.</summary>
    ValueTask QueueAsync(T task, CancellationToken ct = default);

    /// <summary>Dequeues the next task, waiting if necessary.</summary>
    ValueTask<T> DequeueAsync(CancellationToken ct);
}

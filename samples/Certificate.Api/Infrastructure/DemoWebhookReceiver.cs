using System.Collections.Concurrent;

namespace Certificate.Api.Infrastructure;

/// <summary>
/// 仅供演示：模拟外部系统接收 Webhook / HTTP 动作的请求，可设置“接下来 N 次返回 503”来观察自动重试。
/// </summary>
public sealed class DemoWebhookReceiver(TimeProvider clock)
{
    public sealed record Received(string Path, string? IdempotencyKey, string Body, DateTime ReceivedAt);

    private readonly ConcurrentQueue<Received> _received = new();
    private int _failNext;

    public int FailNext => Volatile.Read(ref _failNext);

    public void SetFailNext(int count) => Volatile.Write(ref _failNext, Math.Max(0, count));

    /// <summary>记录请求；需要模拟失败时返回 false（不记录）。</summary>
    public bool TryReceive(string path, string? idempotencyKey, string body)
    {
        while (true)
        {
            var current = Volatile.Read(ref _failNext);
            if (current <= 0) break;
            if (Interlocked.CompareExchange(ref _failNext, current - 1, current) == current) return false;
        }

        _received.Enqueue(new Received(path, idempotencyKey, body, clock.GetUtcNow().UtcDateTime));
        while (_received.Count > 100) _received.TryDequeue(out _);
        return true;
    }

    public IEnumerable<Received> List() => _received.Reverse();

    public void Clear() => _received.Clear();
}

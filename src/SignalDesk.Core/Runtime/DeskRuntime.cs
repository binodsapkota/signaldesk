using System.Collections.Concurrent;
using Microsoft.Extensions.AI;

namespace SignalDesk.Core.Runtime;

/// <summary>Creates conversations and keeps them in memory until they go idle.</summary>
public sealed class DeskRuntime(DeskOptions options) : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, DeskSession> _sessions = new();
    private readonly IChatClient? _liveClient = options.CreateLiveClient();

    public DeskOptions Options { get; } = options;

    public int Count => _sessions.Count;

    public DeskSession Create()
    {
        PruneIdle();
        var session = new DeskSession(Guid.NewGuid().ToString("N")[..12], Options, _liveClient);
        _sessions[session.Id] = session;
        return session;
    }

    public DeskSession? Get(string id) => _sessions.TryGetValue(id, out DeskSession? s) ? s : null;

    public async ValueTask DisposeAsync()
    {
        foreach (DeskSession session in _sessions.Values)
        {
            await session.DisposeAsync();
        }

        _liveClient?.Dispose();
    }

    private void PruneIdle()
    {
        DateTimeOffset cutoff = DateTimeOffset.UtcNow.AddMinutes(-Options.SessionIdleMinutes);
        foreach ((string id, DeskSession session) in _sessions)
        {
            if (session.LastActivity < cutoff && !session.IsBusy && _sessions.TryRemove(id, out _))
            {
                _ = session.DisposeAsync().AsTask();
            }
        }
    }
}

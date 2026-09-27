using System.Collections.Concurrent;
using Microsoft.Extensions.AI;

namespace LeadPilot.Core.Runtime;

/// <summary>Creates workspaces (one CRM sandbox + MCP session each) and keeps them until they go idle.</summary>
public sealed class PilotRuntime(PilotOptions options) : IAsyncDisposable
{
    private const int MaxWorkspaces = 200;

    private readonly ConcurrentDictionary<string, PilotSession> _sessions = new();
    private readonly IChatClient? _liveClient = options.CreateLiveClient();

    public PilotOptions Options { get; } = options;

    public int Count => _sessions.Count;

    public async Task<PilotSession> CreateAsync(CancellationToken ct = default)
    {
        await PruneAsync();
        PilotSession session = await PilotSession.CreateAsync(Guid.NewGuid().ToString("N")[..12], Options, _liveClient, ct);
        _sessions[session.Id] = session;
        return session;
    }

    public PilotSession? Get(string id) => _sessions.TryGetValue(id, out PilotSession? s) ? s : null;

    public async ValueTask DisposeAsync()
    {
        foreach (PilotSession session in _sessions.Values)
        {
            await session.DisposeAsync();
        }

        _liveClient?.Dispose();
    }

    private async Task PruneAsync()
    {
        DateTimeOffset cutoff = DateTimeOffset.UtcNow.AddMinutes(-Options.WorkspaceIdleMinutes);
        IEnumerable<PilotSession> stale = _sessions.Values.Where(s => !s.IsBusy && s.LastActivity < cutoff);
        if (_sessions.Count >= MaxWorkspaces)
        {
            stale = stale.Concat(_sessions.Values.Where(s => !s.IsBusy).OrderBy(s => s.LastActivity).Take(_sessions.Count - MaxWorkspaces + 1));
        }

        foreach (PilotSession session in stale.Distinct().ToList())
        {
            if (_sessions.TryRemove(session.Id, out _)) await session.DisposeAsync();
        }
    }
}

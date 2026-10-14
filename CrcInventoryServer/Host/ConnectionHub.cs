namespace CrcInventory.Server;

/// <summary>Thread-safe list of TLS sessions for the server window.</summary>
internal sealed class ConnectionHub
{
    private readonly object _gate = new();
    private readonly List<ClientSession> _sessions = new();

    /// <summary>Raised on any thread after a connect, sign-in, sign-out, or disconnect.</summary>
    public event Action? Changed;

    /// <summary>Start tracking this TCP/TLS session.</summary>
    public void Add(ClientSession session)
    {
        lock (_gate)
            _sessions.Add(session);
        Changed?.Invoke();
    }

    /// <summary>Stop tracking this session (client dropped or the host stopped).</summary>
    public void Remove(ClientSession session)
    {
        lock (_gate)
            _sessions.Remove(session);
        Changed?.Invoke();
    }

    /// <summary>Sign-in or logout changed identity; refresh the window list.</summary>
    public void Notify() => Changed?.Invoke();

    /// <summary>Copy of current sessions for the connected-users list.</summary>
    public List<ConnectionInfo> Snapshot()
    {
        lock (_gate)
        {
            return _sessions.Select(session => new ConnectionInfo(
                session.Username,
                session.IsAdmin,
                session.IsIt,
                session.Remote,
                session.ConnectedAt)).ToList();
        }
    }
}

/// <summary>One row in the server window's connected-users list.</summary>
internal readonly record struct ConnectionInfo(
    string Username,
    bool IsAdmin,
    bool IsIt,
    string Remote,
    DateTime ConnectedAt);

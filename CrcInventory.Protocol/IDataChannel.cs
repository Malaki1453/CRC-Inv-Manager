namespace CrcInventory.Protocol;

/// <summary>
/// One way to talk to the inventory host. Today that is a TLS named-op stream.
/// Later transports implement this same surface so clients still never touch files.
/// </summary>
public interface IDataChannel : IDisposable
{
    bool IsConnected { get; }

    T Call<T>(string op, object? payload = null);

    bool Try<T>(string op, object? payload, out T? result);
}

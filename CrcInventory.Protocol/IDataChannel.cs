namespace CrcInventory.Protocol;

/// <summary>
/// One way to talk to the inventory host. Today that is a TLS named-op stream.
/// Later transports implement this same surface so clients still never touch files.
/// </summary>
public interface IDataChannel : IDisposable
{
    /// <summary>True while the underlying transport is open and usable.</summary>
    bool IsConnected { get; }

    /// <summary>Sends a named operation and deserializes the success payload as <typeparamref name="T"/>.</summary>
    T Call<T>(string op, object? payload = null);

    /// <summary>Like <see cref="Call{T}"/> but returns false on disconnect or any thrown error.</summary>
    bool Try<T>(string op, object? payload, out T? result);
}

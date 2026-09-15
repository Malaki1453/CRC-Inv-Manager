using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace CrcInventory.Protocol;

/// <summary>
/// Length-prefixed JSON frames on a TLS stream. Layout is 4 big-endian bytes
/// of payload length, then UTF-8 JSON. The framing is independent of TCP vs
/// another byte pipe so a later channel can reuse it.
/// </summary>
public static class Wire
{
    /// <summary>Hard cap on a single frame so a peer cannot force a huge allocation.</summary>
    public const int MaxFrameBytes = 32 * 1024 * 1024;

    /// <summary>Serializes <paramref name="frame"/> to JSON and writes the 4-byte length plus payload.</summary>
    public static async Task WriteAsync(Stream stream, object frame, CancellationToken cancel = default)
    {
        // frame is the WireRequest/WireResponse envelope; json is its UTF-8 payload.
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(frame, JsonWire.Options);
        // Refuse to send a frame that the peer would reject as oversized.
        if (json.Length > MaxFrameBytes)
            throw new InvalidOperationException("Frame is larger than " + MaxFrameBytes + " bytes.");

        byte[] header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, json.Length);
        await stream.WriteAsync(header, cancel).ConfigureAwait(false);
        await stream.WriteAsync(json, cancel).ConfigureAwait(false);
        await stream.FlushAsync(cancel).ConfigureAwait(false);
    }

    /// <summary>Reads one length-prefixed JSON frame and deserializes it as <typeparamref name="T"/>.</summary>
    public static async Task<T?> ReadAsync<T>(Stream stream, CancellationToken cancel = default)
    {
        byte[] header = await ReadExactAsync(stream, 4, cancel).ConfigureAwait(false);
        int length = BinaryPrimitives.ReadInt32BigEndian(header);
        // Negative or huge lengths would allocate badly or never finish; abort the frame.
        if (length < 0 || length > MaxFrameBytes)
            throw new InvalidOperationException("Invalid frame length " + length + ".");

        byte[] json = await ReadExactAsync(stream, length, cancel).ConfigureAwait(false);
        return JsonSerializer.Deserialize<T>(json, JsonWire.Options);
    }

    /// <summary>Builds a request with a new correlation id and a JSON payload (empty object when null).</summary>
    public static WireRequest Request(string op, object? payload)
    {
        JsonElement element = payload == null
            ? JsonSerializer.SerializeToElement(new { }, JsonWire.Options)
            : JsonSerializer.SerializeToElement(payload, JsonWire.Options);

        return new WireRequest
        {
            Version = ServerOps.ProtocolVersion,
            Id = Guid.NewGuid().ToString("N"),
            Op = op,
            Payload = element
        };
    }

    /// <summary>Builds a success response; omits payload when <paramref name="payload"/> is null.</summary>
    public static WireResponse Ok(string id, object? payload)
    {
        JsonElement? element = payload == null
            ? null
            : JsonSerializer.SerializeToElement(payload, JsonWire.Options);

        return new WireResponse
        {
            Version = ServerOps.ProtocolVersion,
            Id = id,
            Ok = true,
            Payload = element
        };
    }

    /// <summary>Builds a failure response with a public error string and no payload.</summary>
    public static WireResponse Fail(string id, string error) => new()
    {
        Version = ServerOps.ProtocolVersion,
        Id = id,
        Ok = false,
        Error = error
    };

    /// <summary>Reads exactly <paramref name="count"/> bytes, throwing if the stream ends early.</summary>
    private static async Task<byte[]> ReadExactAsync(Stream stream, int count, CancellationToken cancel)
    {
        byte[] buffer = new byte[count];
        int offset = 0;
        while (offset < count)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset), cancel)
                .ConfigureAwait(false);
            // Zero bytes means the peer closed before the frame was complete.
            if (read == 0)
                throw new EndOfStreamException();
            offset += read;
        }

        return buffer;
    }

    /// <summary>UTF-8 decode of a JSON buffer, for logging or diagnostics.</summary>
    public static string Describe(byte[] json) => Encoding.UTF8.GetString(json);
}

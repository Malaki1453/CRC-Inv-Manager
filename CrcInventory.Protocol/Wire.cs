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
    public const int MaxFrameBytes = 32 * 1024 * 1024;

    public static async Task WriteAsync(Stream stream, object frame, CancellationToken cancel = default)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(frame, JsonWire.Options);
        if (json.Length > MaxFrameBytes)
            throw new InvalidOperationException("Frame is larger than " + MaxFrameBytes + " bytes.");

        byte[] header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, json.Length);
        await stream.WriteAsync(header, cancel).ConfigureAwait(false);
        await stream.WriteAsync(json, cancel).ConfigureAwait(false);
        await stream.FlushAsync(cancel).ConfigureAwait(false);
    }

    public static async Task<T?> ReadAsync<T>(Stream stream, CancellationToken cancel = default)
    {
        byte[] header = await ReadExactAsync(stream, 4, cancel).ConfigureAwait(false);
        int length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length < 0 || length > MaxFrameBytes)
            throw new InvalidOperationException("Invalid frame length " + length + ".");

        byte[] json = await ReadExactAsync(stream, length, cancel).ConfigureAwait(false);
        return JsonSerializer.Deserialize<T>(json, JsonWire.Options);
    }

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

    public static WireResponse Fail(string id, string error) => new()
    {
        Version = ServerOps.ProtocolVersion,
        Id = id,
        Ok = false,
        Error = error
    };

    private static async Task<byte[]> ReadExactAsync(Stream stream, int count, CancellationToken cancel)
    {
        byte[] buffer = new byte[count];
        int offset = 0;
        while (offset < count)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset), cancel)
                .ConfigureAwait(false);
            if (read == 0)
                throw new EndOfStreamException();
            offset += read;
        }

        return buffer;
    }

    public static string Describe(byte[] json) => Encoding.UTF8.GetString(json);
}

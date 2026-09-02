using CrcInventory.Protocol;

namespace CastRightCatchInvManagement
{
    /// <summary>
    /// How this PC reaches inventory data. Today that is a TLS named-op stream
    /// (<see cref="ServerClient"/>). A later access method implements
    /// <see cref="IDataChannel"/> and plugs in here — the rest of the app does
    /// not change, and clients still never open database files.
    /// </summary>
    internal static class DataLink
    {
        public const int DefaultPort = 7443;

        // Local folder database. To use CrcInventoryServer instead, comment the false line and uncomment true.
        public static readonly bool UseInventoryServer = false;
        // public static readonly bool UseInventoryServer = true;

        private static readonly object Gate = new();
        private static IDataChannel? _channel;

        public static bool IsRemote
        {
            get
            {
                if (!UseInventoryServer)
                    return false;
                lock (Gate)
                    return _channel is { IsConnected: true };
            }
        }

        public static bool HasItUser { get; private set; }

        public static string Fingerprint { get; private set; } = "";

        public static void Connect(string host, int port, string? fingerprint)
        {
            if (!UseInventoryServer)
                throw new InvalidOperationException("The inventory server is turned off in DataLink.");

            host = (host ?? "").Trim();
            if (host.Length == 0)
                throw new InvalidOperationException("Enter the server IP address.");
            if (port <= 0 || port > 65535)
                port = DefaultPort;

            var client = new ServerClient();
            try
            {
                client.Connect(host, port, fingerprint);
                var hello = client.Call<HelloResponse>(ServerOps.SessionHello);
                lock (Gate)
                {
                    _channel?.Dispose();
                    _channel = client;
                    Fingerprint = client.Fingerprint;
                    HasItUser = hello.HasItUser;
                }
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }

        public static void Disconnect()
        {
            lock (Gate)
            {
                _channel?.Dispose();
                _channel = null;
                Fingerprint = "";
                HasItUser = false;
            }
        }

        public static T Call<T>(string op, object? payload = null)
        {
            IDataChannel channel;
            lock (Gate)
            {
                channel = _channel ?? throw new InvalidOperationException("Not connected to the inventory server.");
            }

            return channel.Call<T>(op, payload);
        }

        public static void Send(string op, object? payload = null)
        {
            _ = Call<bool>(op, payload);
        }

        public static bool Try<T>(string op, object? payload, out T? result)
        {
            result = default;
            if (!IsRemote)
                return false;

            try
            {
                result = Call<T>(op, payload);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static TableRequest Table(
            string table,
            Dictionary<string, string>? values = null,
            long id = 0,
            IEnumerable<Dictionary<string, string>>? rows = null,
            string[]? columns = null,
            DateTime? term = null,
            bool currentTermOnly = false)
        {
            return new TableRequest
            {
                Table = table,
                ViewOld = AppState.ViewingOldInventory,
                CurrentTermOnly = currentTermOnly,
                Id = id,
                Values = values,
                Rows = rows?.ToList(),
                Columns = columns,
                Term = term?.ToString("yyyy-MM-dd")
            };
        }

        /// <summary>
        /// Split "host", "host:port", or "ip:port". Later auto-discovery can
        /// fill host without showing this box.
        /// </summary>
        public static void ParseEndpoint(string? text, out string host, out int port)
        {
            host = "";
            port = DefaultPort;
            text = (text ?? "").Trim();
            if (text.Length == 0)
                return;

            int colon = text.LastIndexOf(':');
            if (colon > 0 &&
                colon < text.Length - 1 &&
                int.TryParse(text[(colon + 1)..], out int parsed) &&
                parsed > 0 && parsed <= 65535 &&
                text.IndexOf(':') == colon)
            {
                host = text[..colon].Trim();
                port = parsed;
                return;
            }

            host = text;
        }
    }
}

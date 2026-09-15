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

        /// <summary>True when this PC is connected to CrcInventoryServer instead of a local folder.</summary>
        public static bool IsRemote
        {
            get
            {
                // Local-folder builds never talk to the named-op stream.
                if (!UseInventoryServer)
                    return false;
                lock (Gate)
                    return _channel is { IsConnected: true };
            }
        }

        public static bool HasItUser { get; private set; }

        public static string Fingerprint { get; private set; } = "";

        /// <summary>Open a TLS session to the inventory server and record the hello handshake.</summary>
        public static void Connect(string host, int port, string? fingerprint)
        {
            // The flag is the only switch between local SQLite and the server.
            if (!UseInventoryServer)
                throw new InvalidOperationException("The inventory server is turned off in DataLink.");

            host = (host ?? "").Trim();
            // A blank host cannot resolve; Settings must supply an address.
            if (host.Length == 0)
                throw new InvalidOperationException("Enter the server IP address.");
            // Out-of-range ports fall back to the app's default TLS port.
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
                // Drop a half-open client so a failed connect cannot leak sockets.
                client.Dispose();
                throw;
            }
        }

        /// <summary>Close the current server session and clear handshake state.</summary>
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

        /// <summary>Send a named op and deserialize the reply. Throws if no session is open.</summary>
        public static T Call<T>(string op, object? payload = null)
        {
            IDataChannel channel;
            lock (Gate)
            {
                channel = _channel ?? throw new InvalidOperationException("Not connected to the inventory server.");
            }

            return channel.Call<T>(op, payload);
        }

        /// <summary>Fire a named op whose reply is only an acknowledgement.</summary>
        public static void Send(string op, object? payload = null)
        {
            _ = Call<bool>(op, payload);
        }

        /// <summary>Call a named op when remote; return false on local mode or any transport error.</summary>
        public static bool Try<T>(string op, object? payload, out T? result)
        {
            result = default;
            // Local SQLite callers should not hit the network.
            if (!IsRemote)
                return false;

            try
            {
                result = Call<T>(op, payload);
                return true;
            }
            catch
            {
                // A downed server should not crash table reads; callers fall back or skip.
                return false;
            }
        }

        /// <summary>Build the standard table payload, including Current/Old view flags.</summary>
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
            // Empty Settings text means "use defaults" rather than a bad address.
            if (text.Length == 0)
                return;

            int colon = text.LastIndexOf(':');
            // Treat a single trailing :port as an explicit port, not an IPv6 address.
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

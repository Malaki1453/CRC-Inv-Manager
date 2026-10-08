namespace CrcInventory.Protocol;

/// <summary>
/// Public DNS name clients always use. No IP is typed in the desktop app.
/// Point an A (or AAAA) record at the machine that runs CrcInventoryServer, TCP 7443.
/// </summary>
public static class InventoryHost
{
    /// <summary>Hostname every client resolves. Change this if the company uses a different name.</summary>
    public const string DnsName = "inventory.castrightcatch.com";

    /// <summary>TLS listen port on the host and the port clients connect to.</summary>
    public const int Port = 7443;

    /// <summary>True when the saved host is this PC (loopback), not the company DNS name.</summary>
    public static bool IsThisComputer(string? host)
    {
        host = (host ?? "").Trim();
        return host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
            || host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.Equals("::1", StringComparison.OrdinalIgnoreCase);
    }
}

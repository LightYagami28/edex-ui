// SPDX-License-Identifier: GPL-3.0-only
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Edex.Core.Networking;

public sealed record NetworkSnapshot(
    bool Online,
    string InterfaceName,
    string? IPv4,
    string? Gateway,
    string? MacAddress,
    double DownloadBytesPerSecond,
    double UploadBytesPerSecond,
    long TotalReceivedBytes,
    long TotalSentBytes,
    double? PingMilliseconds,
    int ActiveTcpConnections);

/// <summary>Samples the primary network interface. Call <see cref="SampleAsync"/> about once per second.</summary>
public sealed class NetworkMonitor
{
    private readonly string _pingHost;
    private long _lastRx = -1, _lastTx = -1;
    private DateTime _lastSample;
    private string? _lastInterface;
    private double? _lastPing;
    private DateTime _lastPingTime = DateTime.MinValue;

    public NetworkMonitor(string pingHost = "1.1.1.1") => _pingHost = pingHost;

    public async Task<NetworkSnapshot> SampleAsync(CancellationToken cancellationToken = default)
    {
        var nic = FindPrimaryInterface();
        if (nic is null)
            return new NetworkSnapshot(false, "offline", null, null, null, 0, 0, 0, 0, null, 0);

        var props = nic.GetIPProperties();
        var ipv4 = props.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString();
        var gateway = props.GatewayAddresses.FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any))?.Address.ToString();
        var mac = FormatMac(nic.GetPhysicalAddress());

        long rx = 0, tx = 0;
        try
        {
            var stats = nic.GetIPStatistics();
            rx = stats.BytesReceived;
            tx = stats.BytesSent;
        }
        catch (Exception e) when (e is NetworkInformationException or PlatformNotSupportedException) { }

        var now = DateTime.UtcNow;
        double down = 0, up = 0;
        if (_lastInterface == nic.Id && _lastRx >= 0)
        {
            double seconds = Math.Max(0.001, (now - _lastSample).TotalSeconds);
            down = Math.Max(0, (rx - _lastRx) / seconds);
            up = Math.Max(0, (tx - _lastTx) / seconds);
        }
        _lastInterface = nic.Id;
        _lastRx = rx;
        _lastTx = tx;
        _lastSample = now;

        if ((now - _lastPingTime).TotalSeconds >= 5)
        {
            _lastPingTime = now;
            _lastPing = await PingAsync(cancellationToken).ConfigureAwait(false);
        }

        return new NetworkSnapshot(true, nic.Name, ipv4, gateway, mac, down, up, rx, tx, _lastPing, CountTcpConnections());
    }

    private async Task<double?> PingAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(IPAddress.Parse(_pingHost), TimeSpan.FromSeconds(1), cancellationToken: cancellationToken).ConfigureAwait(false);
            return reply.Status == IPStatus.Success ? reply.RoundtripTime : null;
        }
        catch (Exception e) when (e is PingException or InvalidOperationException or PlatformNotSupportedException or OperationCanceledException)
        {
            return null;
        }
    }

    private static int CountTcpConnections()
    {
        try
        {
            return IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections().Count(c => c.State == TcpState.Established);
        }
        catch (Exception e) when (e is NetworkInformationException or PlatformNotSupportedException)
        {
            return 0;
        }
    }

    internal static NetworkInterface? FindPrimaryInterface()
    {
        NetworkInterface[] all;
        try { all = NetworkInterface.GetAllNetworkInterfaces(); }
        catch (NetworkInformationException) { return null; }

        var candidates = all
            .Where(n => n.OperationalStatus == OperationalStatus.Up
                        && n.NetworkInterfaceType is not NetworkInterfaceType.Loopback and not NetworkInterfaceType.Tunnel)
            .Select(n => (Nic: n, Props: n.GetIPProperties()))
            .Where(x => x.Props.UnicastAddresses.Any(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address)))
            .ToList();

        // Prefer an interface with a default gateway (the one actually used for the internet).
        return candidates
            .OrderByDescending(x => x.Props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)))
            .ThenByDescending(x => x.Nic.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211)
            .Select(x => x.Nic)
            .FirstOrDefault();
    }

    internal static string? FormatMac(PhysicalAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes.Length == 0 ? null : string.Join(':', bytes.Select(b => b.ToString("x2")));
    }
}

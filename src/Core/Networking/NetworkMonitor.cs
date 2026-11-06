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

/// <summary>Plain data about one network interface.</summary>
public sealed record NicInfo(
    string Id,
    string Name,
    NetworkInterfaceType Type,
    OperationalStatus Status,
    IReadOnlyList<IPAddress> Addresses,
    IReadOnlyList<IPAddress> Gateways,
    byte[] Mac,
    long BytesReceived,
    long BytesSent);

/// <summary>Where the monitor reads the system from. The real implementation is <see cref="SystemNetworkProbe"/>.</summary>
public interface INetworkProbe
{
    IReadOnlyList<NicInfo> Interfaces();
    int EstablishedConnections();
    Task<double?> PingAsync(string host, CancellationToken cancellationToken);
}

/// <summary>Reads interfaces, TCP connections and ping from the operating system.</summary>
public sealed class SystemNetworkProbe : INetworkProbe
{
    public IReadOnlyList<NicInfo> Interfaces() =>
        Safe.Run<IReadOnlyList<NicInfo>>(() => NetworkInterface.GetAllNetworkInterfaces().Select(Describe).ToList(), []);

    private static NicInfo Describe(NetworkInterface nic)
    {
        var props = nic.GetIPProperties();
        var (received, sent) = Safe.Run(() =>
        {
            var stats = nic.GetIPStatistics();
            return (stats.BytesReceived, stats.BytesSent);
        }, (0L, 0L));
        return new NicInfo(
            nic.Id,
            nic.Name,
            nic.NetworkInterfaceType,
            nic.OperationalStatus,
            props.UnicastAddresses.Select(a => a.Address).ToList(),
            props.GatewayAddresses.Select(g => g.Address).ToList(),
            nic.GetPhysicalAddress().GetAddressBytes(),
            received,
            sent);
    }

    public int EstablishedConnections() =>
        Safe.Run(() => IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections().Count(c => c.State == TcpState.Established), 0);

    public async Task<double?> PingAsync(string host, CancellationToken cancellationToken)
    {
        if (!IPAddress.TryParse(host, out var address)) return null;
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(address, TimeSpan.FromSeconds(1), cancellationToken: cancellationToken).ConfigureAwait(false);
            return reply.Status == IPStatus.Success ? reply.RoundtripTime : null;
        }
        catch (Exception e) when (e is PingException or InvalidOperationException or PlatformNotSupportedException or OperationCanceledException)
        {
            return null;
        }
    }
}

/// <summary>Samples the primary network interface. Call <see cref="SampleAsync"/> about once per second.</summary>
public sealed class NetworkMonitor
{
    private readonly string _pingHost;
    private readonly INetworkProbe _probe;
    private readonly Func<DateTime> _clock;
    private long _lastRx = -1, _lastTx = -1;
    private DateTime _lastSample;
    private string? _lastInterface;
    private double? _lastPing;
    private DateTime _lastPingTime = DateTime.MinValue;

    public NetworkMonitor(string pingHost = "one.one.one.one", INetworkProbe? probe = null, Func<DateTime>? clock = null)
    {
        _pingHost = pingHost;
        _probe = probe ?? new SystemNetworkProbe();
        _clock = clock ?? (() => DateTime.UtcNow);
    }

    public async Task<NetworkSnapshot> SampleAsync(CancellationToken cancellationToken = default)
    {
        var nic = FindPrimaryInterface(_probe.Interfaces());
        if (nic is null)
            return new NetworkSnapshot(false, "offline", null, null, null, 0, 0, 0, 0, null, 0);

        var ipv4 = nic.Addresses.FirstOrDefault(IsIPv4)?.ToString();
        var gateway = nic.Gateways.FirstOrDefault(IsUsableGateway)?.ToString();

        var now = _clock();
        double down = 0, up = 0;
        if (_lastInterface == nic.Id && _lastRx >= 0)
        {
            double seconds = Math.Max(0.001, (now - _lastSample).TotalSeconds);
            down = Math.Max(0, (nic.BytesReceived - _lastRx) / seconds);
            up = Math.Max(0, (nic.BytesSent - _lastTx) / seconds);
        }
        _lastInterface = nic.Id;
        _lastRx = nic.BytesReceived;
        _lastTx = nic.BytesSent;
        _lastSample = now;

        if ((now - _lastPingTime).TotalSeconds >= 5)
        {
            _lastPingTime = now;
            _lastPing = await _probe.PingAsync(_pingHost, cancellationToken).ConfigureAwait(false);
        }

        return new NetworkSnapshot(true, nic.Name, ipv4, gateway, FormatMac(nic.Mac), down, up, nic.BytesReceived, nic.BytesSent, _lastPing, _probe.EstablishedConnections());
    }

    private static bool IsIPv4(IPAddress address) => address.AddressFamily == AddressFamily.InterNetwork;

    private static bool IsUsableGateway(IPAddress address) => IsIPv4(address) && !address.Equals(IPAddress.Any);

    /// <summary>The interface used for the internet: up, with an IPv4 address, preferring a default gateway and then Ethernet or Wi-Fi.</summary>
    public static NicInfo? FindPrimaryInterface(IEnumerable<NicInfo> interfaces) => interfaces
        .Where(n => n.Status == OperationalStatus.Up
                    && n.Type is not NetworkInterfaceType.Loopback and not NetworkInterfaceType.Tunnel
                    && n.Addresses.Any(a => IsIPv4(a) && !IPAddress.IsLoopback(a)))
        .OrderByDescending(n => n.Gateways.Any(IsUsableGateway))
        .ThenByDescending(n => n.Type is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211)
        .FirstOrDefault();

    public static string? FormatMac(byte[] bytes) =>
        bytes.Length == 0 ? null : string.Join(':', bytes.Select(b => b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));
}

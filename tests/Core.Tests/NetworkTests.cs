// SPDX-License-Identifier: GPL-3.0-only
using System.Net;
using System.Net.NetworkInformation;
using Edex.Core.Networking;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Edex.Core.Tests;

[TestClass]
public class NetworkTests
{
    private sealed class FakeProbe(params NicInfo[] nics) : INetworkProbe
    {
        public List<NicInfo> Nics { get; } = [.. nics];
        public int Pings { get; private set; }
        public double? PingResult { get; set; } = 12;

        public IReadOnlyList<NicInfo> Interfaces() => Nics;
        public int EstablishedConnections() => 7;

        public Task<double?> PingAsync(string host, CancellationToken cancellationToken)
        {
            Pings++;
            return Task.FromResult(PingResult);
        }
    }

    private static NicInfo Nic(string id, NetworkInterfaceType type = NetworkInterfaceType.Ethernet, OperationalStatus status = OperationalStatus.Up,
        string address = "192.168.1.5", string? gateway = "192.168.1.1", long rx = 0, long tx = 0, byte[]? mac = null) => new(
        id, "nic-" + id, type, status,
        address.Length == 0 ? [] : [IPAddress.Parse(address)],
        gateway is null ? [] : [IPAddress.Parse(gateway)],
        mac ?? [0, 0x1a, 0xff, 2, 3, 4], rx, tx);

    [TestMethod]
    public void PrimaryInterfacePrefersGatewayThenEthernet()
    {
        Assert.IsNull(NetworkMonitor.FindPrimaryInterface([]));
        var down = Nic("down", status: OperationalStatus.Down);
        var loop = Nic("loop", NetworkInterfaceType.Loopback, address: "127.0.0.1");
        var tunnel = Nic("tunnel", NetworkInterfaceType.Tunnel);
        var v6Only = new NicInfo("v6", "v6", NetworkInterfaceType.Ethernet, OperationalStatus.Up, [IPAddress.IPv6Loopback], [], [], 0, 0);
        var loopAddr = Nic("loopaddr", address: "127.0.0.1");
        Assert.IsNull(NetworkMonitor.FindPrimaryInterface([down, loop, tunnel, v6Only, loopAddr]));

        var noGateway = Nic("nogw", gateway: null);
        var anyGateway = Nic("anygw", gateway: "0.0.0.0");
        var other = Nic("other", NetworkInterfaceType.Ppp, gateway: null);
        var wifi = Nic("wifi", NetworkInterfaceType.Wireless80211, gateway: null);
        Assert.AreEqual("anygw", NetworkMonitor.FindPrimaryInterface([other, anyGateway, noGateway, wifi])?.Id);
        Assert.AreEqual("wifi", NetworkMonitor.FindPrimaryInterface([other, wifi])?.Id);
        Assert.AreEqual("other", NetworkMonitor.FindPrimaryInterface([other])?.Id);
        Assert.AreEqual("gw", NetworkMonitor.FindPrimaryInterface([noGateway, other, Nic("gw", NetworkInterfaceType.Ppp)])?.Id);
    }

    [TestMethod]
    public void FormatsMac()
    {
        Assert.IsNull(NetworkMonitor.FormatMac([]));
        Assert.AreEqual("00:1a:ff:02", NetworkMonitor.FormatMac([0, 0x1a, 0xff, 2]));
    }

    [TestMethod]
    public async Task OfflineWithoutInterface()
    {
        var monitor = new NetworkMonitor(probe: new FakeProbe());
        var snapshot = await monitor.SampleAsync();
        Assert.IsFalse(snapshot.Online);
        Assert.AreEqual("offline", snapshot.InterfaceName);
    }

    [TestMethod]
    public async Task ComputesRatesAndThrottlesPing()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var probe = new FakeProbe(Nic("a", rx: 1000, tx: 500));
        var monitor = new NetworkMonitor("9.9.9.9", probe, () => now);

        var first = await monitor.SampleAsync();
        Assert.IsTrue(first.Online);
        Assert.AreEqual("192.168.1.5", first.IPv4);
        Assert.AreEqual("192.168.1.1", first.Gateway);
        Assert.AreEqual("00:1a:ff:02:03:04", first.MacAddress);
        Assert.AreEqual(0, first.DownloadBytesPerSecond);
        Assert.AreEqual(12, first.PingMilliseconds);
        Assert.AreEqual(7, first.ActiveTcpConnections);
        Assert.AreEqual(1, probe.Pings);

        now = now.AddSeconds(2);
        probe.Nics[0] = Nic("a", rx: 5000, tx: 1500);
        probe.PingResult = 99;
        var second = await monitor.SampleAsync();
        Assert.AreEqual(2000, second.DownloadBytesPerSecond);
        Assert.AreEqual(500, second.UploadBytesPerSecond);
        Assert.AreEqual(5000, second.TotalReceivedBytes);
        Assert.AreEqual(12, second.PingMilliseconds);
        Assert.AreEqual(1, probe.Pings);

        now = now.AddSeconds(6);
        probe.Nics[0] = Nic("a", rx: 100, tx: 100);
        var third = await monitor.SampleAsync();
        Assert.AreEqual(0, third.DownloadBytesPerSecond);
        Assert.AreEqual(99, third.PingMilliseconds);
        Assert.AreEqual(2, probe.Pings);

        now = now.AddSeconds(1);
        probe.Nics[0] = Nic("b", rx: 9000, tx: 9000);
        var other = await monitor.SampleAsync();
        Assert.AreEqual(0, other.DownloadBytesPerSecond);
    }

    [TestMethod]
    public async Task SystemProbeReadsThisMachine()
    {
        var probe = new SystemNetworkProbe();
        Assert.IsNotNull(probe.Interfaces());
        Assert.IsGreaterThanOrEqualTo(0, probe.EstablishedConnections());
        Assert.IsNull(await probe.PingAsync("not an address", CancellationToken.None));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        Assert.IsNull(await probe.PingAsync("127.0.0.1", cancelled.Token));
        var local = await probe.PingAsync("127.0.0.1", CancellationToken.None);
        Assert.IsTrue(local is null or >= 0);
        var unreachable = await probe.PingAsync("192.0.2.1", CancellationToken.None);
        Assert.IsTrue(unreachable is null or >= 0);
        var sample = await new NetworkMonitor("127.0.0.1").SampleAsync();
        Assert.IsNotNull(sample.InterfaceName);
    }
}

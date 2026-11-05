using System;
using System.Linq;
using System.Net;

using Microsoft.Extensions.Logging;

using Lunarite.Handlers;
using Lunarite.Network;
using Lunarite.Packets.Incoming;
using Lunarite.Packets.Outgoing;
using Lunarite.Resources;
using Lunarite.Storage;

namespace Lunarite.World;

public sealed class P2PManager
{
    private readonly ILogger _logger;

    private readonly CityZoneCatalog _cityZoneCatalog;

    public P2PManager(ILogger<P2PManager> logger, CityZoneCatalog cityZoneCatalog)
    {
        _logger = logger;
        _cityZoneCatalog = cityZoneCatalog;
    }

    public P2PNatTestService? NatTestService { get; set; }

    // Public UDP endpoint of a listen host: the authenticated op155 NAT report when present, else
    // the relay-observed reflexive address from op196 (UDPSocketHelper "MyAddr"). The relay sees
    // same-PC clients as 127.0.0.1 regardless of their -MULTIHOME address, so a loopback op196
    // address is ignored and the caller keeps its TCP-derived address.
    public static (uint Ip, ushort Port)? ReportedPublicEndpoint(P2PNatEndpointReport? nat,
        P2PNetworkAddressesReport? relay)
    {
        if (nat is { PublicAddress: not 0, PublicPort: not 0 })
            return (unchecked((uint)nat.PublicAddress), nat.PublicPort);
        if (relay?.Entries.FirstOrDefault(x => x.MyAddress != 0 && x.MyPort != 0
                && (x.MyAddress & 0xFF) != 127) is { MyAddress: not 0 } seen)
            return (unchecked((uint)seen.MyAddress), seen.MyPort);
        return null;
    }

    public static IPAddress? ReportedNatLocalAddress(int packedAddress)
    {
        if (packedAddress == 0) return null;
        uint value = unchecked((uint)packedAddress);
        return new IPAddress([(byte)value, (byte)(value >> 8),
            (byte)(value >> 16), (byte)(value >> 24)]);
    }

    // A host on the server's own PC is seen at 127.x. A remote guest given that address would
    // connect to itself, so it gets the server address on the interface it connected through
    // (e.g. a Radmin VPN IP), where the host's P2P socket also listens.
    public static uint ReachableHostIp(uint hostIp, bool viewerIsLocal, uint viewerFacingServerIp) =>
        (hostIp & 0xFF) == 127 && !viewerIsLocal && viewerFacingServerIp != 0
            && (viewerFacingServerIp & 0xFF) != 127 ? viewerFacingServerIp : hostIp;

    internal static uint HostIpFor(uint hostIp, Client viewer) => ReachableHostIp(hostIp,
        System.Net.IPAddress.IsLoopback(viewer.IPAddress),
        PackedIp(viewer.LocalIPAddress.MapToIPv4().ToString()));

    internal static PeerAddrList HostEndpointsFor(PeerAddrList host, Client viewer) => new()
    {
        PublicEndpoint = host.PublicEndpoint with
            { Address = unchecked((int)HostIpFor(unchecked((uint)host.PublicEndpoint.Address), viewer)) },
        PeerEndpoint = host.PeerEndpoint with
            { Address = unchecked((int)HostIpFor(unchecked((uint)host.PeerEndpoint.Address), viewer)) }
    };

    // op128/op124 body for a peer's P2P socket: its NAT-reported public endpoint (or the
    // override / TCP address) and its reported local endpoint.
    internal static PeerAddrList BuildClientPeerEndpoints(Client peer, string? publicOverride,
        uint fallbackIp)
    {
        var nat = peer.Session.NatEndpoint;
        var relay = peer.Session.NetworkAddresses;
        var reportedPublic = ReportedPublicEndpoint(nat, relay);
        uint publicIp = QuestInstanceManager.QuestHostPublicIp(publicOverride, unchecked((int)(reportedPublic?.Ip ?? 0)), fallbackIp);
        var local = peer.Session.LocalAddress;
        local = ReportedNatLocalAddress(nat?.LocalAddress ?? 0) ?? local;
        return QuestInstanceManager.BuildQuestHostPeerEndpoints(publicIp, QuestInstanceManager.QuestHostPeerIp(local, publicIp),
            reportedPublic?.Port ?? 7777, nat?.LocalPort ?? 7777);
    }

    public (int Map, int Area) RetainedCityDestination(int currentMap,
        (int Map, int Area) saved, byte characterIndex)
    {
        int area = currentMap == saved.Map ? saved.Area : 0;
        if (_cityZoneCatalog.IsValidArea(currentMap, area, characterIndex))
            return (currentMap, area);
        return (CharacterStore.TrainingCenterMap(characterIndex), 0);
    }

    // "a.b.c.d" -> uint with a in the low byte (127.0.0.1 = 0x0100007F). The client prints the
    // op52 IP low byte first in its `Browse: <ip>/...` log line.
    internal static uint PackedIp(string dotted)
    {
        var parts = dotted.Split('.');
        if (parts.Length != 4) return 0x0100007F;
        return ((uint)byte.Parse(parts[3]) << 24) | ((uint)byte.Parse(parts[2]) << 16)
             | ((uint)byte.Parse(parts[1]) << 8) | byte.Parse(parts[0]);
    }

    // The guest's UE3 connection takes its destination from the P2P socket's peer address,
    // which only the hole-punch test ACK fills in (FP2PSocket::RecvTestACKHolePunchingPacket
    // -> sub_111D2B40, followed by client op118). Before that the peer address is empty and
    // resolves to the guest's own machine, which hangs a remote guest. The join (op52 mission,
    // op156 Simulation) therefore waits for op118, with a timeout. Home-to-home punches miss often
    // (live 2026-11-05: two misses in a row, then the next try worked), so a caller can pass
    // reprobe, which re-sends the endpoints (op128 queues a fresh probe) before each extra round.
    internal async System.Threading.Tasks.Task SendAfterHolePunch(Client client,
        int pid, int hostPid, IPacketSerialize join, string joinName, Action? reprobe = null)
    {
        int waitMs = int.TryParse(Environment.GetEnvironmentVariable("QUEST_HOLEPUNCH_WAIT_MS"), out int configured)
            && configured >= 0 ? configured : 3000;
        int rounds = reprobe == null ? 1
            : int.TryParse(Environment.GetEnvironmentVariable("QUEST_HOLEPUNCH_ATTEMPTS"), out int attempts)
                && attempts > 0 ? attempts : 4;
        int round = 0;
        while (++round <= rounds)
        {
            if (round > 1)
            {
                if (!Game.TryGetClient(pid, out var waiting) || !ReferenceEquals(waiting, client)) return;
                _logger.LogInformation("[P2P] no hole-punch ACK from guest PID {0} yet; probing host PID {1} again ({2}/{3}).",
                    pid, hostPid, round, rounds);
                reprobe!();
            }
            var deadline = DateTime.UtcNow.AddMilliseconds(waitMs);
            while (!client.Session.HolePunchAcknowledged && DateTime.UtcNow < deadline)
                await System.Threading.Tasks.Task.Delay(25);
            if (client.Session.HolePunchAcknowledged) break;
        }
        if (!Game.TryGetClient(pid, out var current) || !ReferenceEquals(current, client)) return;
        bool acknowledged = client.Session.HolePunchAcknowledged;
        client.SendPacket(Game.P2PDirectHoleResult);
        client.SendPacket(join);
        _logger.LogInformation("[P2P] sent guest PID {0} {1} for host PID {2} ({3}).", pid, joinName,
            hostPid, acknowledged ? "after hole-punch ACK" : $"no hole-punch ACK after {Math.Min(round, rounds)} x {waitMs} ms");
    }
}

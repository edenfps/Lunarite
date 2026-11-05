using System.Net;

using Microsoft.Extensions.Logging;

using Lunarite.Handlers;
using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Storage;

internal sealed partial class TestSuite
{
    private static byte[] GameConnectWire(IPAddress local) => [0, .. local.GetAddressBytes(), .. new byte[12]];

    // Peer sees the server close the link (EOF) rather than a quiet open socket.
    private static bool SocialPeerClosed(SocialLink link)
    {
        var socket = link.Peer.Client;
        var sink = new byte[4096];
        while (socket.Poll(1_000_000, System.Net.Sockets.SelectMode.SelectRead))
        {
            try { if (socket.Receive(sink) == 0) return true; }
            catch (System.Net.Sockets.SocketException) { return true; }
        }
        return false;
    }

    private void GameHandoffBinding()
    {
        // Tickets: a stray Connect does not spoil the address's next handoff, and a repeated
        // SelectServer keeps only the account's newest handoff.
        var probed = IPAddress.Parse("192.0.2.40");
        Check(!_accountSessionStore.TryClaim(probed, out _), "Connect with no handoff ticket is refused");
        _accountSessionStore.Issue(probed, 958800101);
        Check(_accountSessionStore.TryClaim(probed, out int afterMiss) && afterMiss == 958800101
            && !_accountSessionStore.TryClaim(probed, out _),
            "a Connect that found no ticket does not spoil the next handoff issued to that address");
        var firstAddress = IPAddress.Parse("192.0.2.41");
        var secondAddress = IPAddress.Parse("192.0.2.42");
        _accountSessionStore.Issue(firstAddress, 958800102);
        _accountSessionStore.Issue(secondAddress, 958800102);
        Check(!_accountSessionStore.TryClaim(firstAddress, out _)
            && _accountSessionStore.TryClaim(secondAddress, out int newest) && newest == 958800102,
            "one outstanding Game handoff per account: a repeated SelectServer replaces the earlier ticket");

        string? previousTrust = Environment.GetEnvironmentVariable("HUXLEY_TRUST_NAT_LOCAL_IP");
        Environment.SetEnvironmentVariable("HUXLEY_TRUST_NAT_LOCAL_IP", "1");
        var localA = IPAddress.Parse("10.77.0.1");
        var localB = IPAddress.Parse("10.77.0.2");
        var localC = IPAddress.Parse("10.77.0.3");
        var localD = IPAddress.Parse("10.77.0.4");
        const int userX = 958800111, userY = 958800112, userZ = 958800113;
        using var linkA = OpenSocialGameSocket(958800001);
        using var linkB = OpenSocialGameSocket(958800002);
        using var linkC = OpenSocialGameSocket(958800003);
        using var linkD = OpenSocialGameSocket(958800004);
        using var linkE = OpenSocialGameSocket(958800005);
        try
        {
            _accountSessionStore.Issue(IPAddress.Loopback, userX, localA, trustClientLocalAddress: true);
            Game.OnHandlePacket(linkA.Client, GameConnectWire(localA));
            Check(linkA.Client.Session.AccountId == userX, "a Game link claims its Agent handoff with Connect");

            _accountSessionStore.Issue(IPAddress.Loopback, userY, localA, trustClientLocalAddress: true);
            Game.OnHandlePacket(linkA.Client, GameConnectWire(localA));
            Check(linkA.Client.Session.AccountId == userX && SocialPeerClosed(linkA),
                "a second Connect on a bound Game link cannot switch it to another account's ticket");
            _accountSessionStore.TryClaim(IPAddress.Loopback, out _, localA, trustClientLocalAddress: true);

            // A live, bound link for userZ; its reopened Game socket (city travel, quest return)
            // takes over and the older link is closed.
            _accountSessionStore.Issue(IPAddress.Loopback, userZ, localB, trustClientLocalAddress: true);
            Game.OnHandlePacket(linkB.Client, GameConnectWire(localB));
            bool firstBound = linkB.Client.Session.AccountId == userZ && _playerManager.Links.Contains(linkB.Client);
            _accountSessionStore.Issue(IPAddress.Loopback, userZ, localD, trustClientLocalAddress: true);
            Game.OnHandlePacket(linkE.Client, GameConnectWire(localD));
            Check(firstBound && linkE.Client.Session.AccountId == userZ
                && _playerManager.Links.Count(link => link.Session.AccountId == userZ) == 1
                && _playerManager.Links.Contains(linkE.Client) && SocialPeerClosed(linkB),
                "one Game link per account: a newer Connect for the account closes the older link instead of being refused");

            Game.OnHandlePacket(linkC.Client, GameConnectWire(localC));
            bool strayClosed = SocialPeerClosed(linkC) && linkC.Client.Session.AccountId == null;
            _accountSessionStore.Issue(IPAddress.Loopback, userY, localC, trustClientLocalAddress: true);
            Game.OnHandlePacket(linkD.Client, GameConnectWire(localC));
            Check(strayClosed && linkD.Client.Session.AccountId == userY,
                "a Connect without a ticket closes its link; the owner's Connect after SelectServer still binds");
        }
        finally
        {
            Environment.SetEnvironmentVariable("HUXLEY_TRUST_NAT_LOCAL_IP", previousTrust);
            foreach (var link in new[] { linkA, linkB, linkC, linkD, linkE }) _playerManager.RemoveLink(link.Client);
        }
    }

    // A neighbour behind the same public IP who blind-polls Game Connect gets a bounded number
    // of tries; collisions it causes buy no extra tries; a paced or stray Connect does not lock
    // the address out; families logging in together are not limited.
    private void HandoffConnectBudget()
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var store = new AccountSessionStore { Clock = () => now };
        var shared = IPAddress.Parse("198.51.100.7");
        const int attackerA = 958860001, attackerB = 958860002, victim = 958860999;
        for (int i = 0; i < 50; i++)
        {
            store.Issue(shared, attackerA);
            store.Issue(shared, attackerB);
            store.TryClaim(shared, out _);
            now += TimeSpan.FromMilliseconds(10);
        }
        for (int i = 0; i < 40; i++)
        {
            store.TryClaim(shared, out _);
            now += TimeSpan.FromMilliseconds(10);
        }
        store.Issue(shared, victim);
        bool attackerTook = store.TryClaim(shared, out int polled) && polled == victim;
        // Once the poller stops and its own tickets lapse, the neighbour gets in.
        now += AccountSessionStore.TicketLifetime;
        store.Issue(shared, victim);
        Check(!attackerTook && store.TryClaim(shared, out int owner) && owner == victim,
            "blind Game Connect polls and self-made collisions spend the address's Connect budget, so the poll after a neighbour's SelectServer cannot take its handoff");

        var budget = IPAddress.Parse("198.51.100.10");
        int refused = 0;
        for (int i = 0; i < AccountSessionStore.ConnectBurst; i++)
            if (!store.TryClaim(budget, out _)) refused++;
        store.Issue(budget, victim);
        bool spent = refused == AccountSessionStore.ConnectBurst && !store.TryClaim(budget, out _);
        // The over-budget Connect left the ticket in place; one refill later its owner claims it.
        now += AccountSessionStore.ConnectRefill;
        Check(spent && store.TryClaim(budget, out int refilled) && refilled == victim,
            "an address gets ConnectBurst Connects that claim nothing, then one per refill, and a refused-for-budget ticket is kept");

        var paced = IPAddress.Parse("198.51.100.8");
        bool pacedOwnerIn = true;
        for (int i = 0; i < 30; i++)
        {
            pacedOwnerIn &= !store.TryClaim(paced, out _);
            now += AccountSessionStore.ConnectRefill;
            store.Issue(paced, victim);
            pacedOwnerIn &= store.TryClaim(paced, out int pacedOwner) && pacedOwner == victim;
        }
        Check(pacedOwnerIn, "a stray Connect every refill interval from the same address never locks its owner out");

        var family = IPAddress.Parse("198.51.100.9");
        int joined = 0;
        for (int user = 958860100; user < 958860100 + 3 * AccountSessionStore.ConnectBurst; user++)
        {
            store.Issue(family, user);
            if (store.TryClaim(family, out int claimed) && claimed == user) joined++;
        }
        Check(joined == 3 * AccountSessionStore.ConnectBurst,
            "successful Game handoffs cost no Connect budget, so a household or LAN party all gets in at once");
    }

    private void SelectionGateAndPendingDeletion()
    {
        Check(Game.IsAllowedBeforeSelection(0, false) && !Game.IsAllowedBeforeSelection(1, false)
            && Game.IsAllowedBeforeSelection(14, true) && Game.IsAllowedBeforeSelection(197, true)
            && !Game.IsAllowedBeforeSelection(64, true) && !Game.IsAllowedBeforeSelection(148, true)
            && !Game.IsAllowedBeforeSelection(146, true),
            "Game opcodes before character selection are limited to the select-scene set");

        const int owner = 958810001;
        using var link = OpenSocialGameSocket(958810002);
        link.Client.Session.AccountId = owner;
        // An unselected link would buy as phantom PID 0 and get a BuyItem answer.
        Game.OnHandlePacket(link.Client, [64, 0x9F, 0x0D, 0x03, 0x00, 0, 1, 0, 0, 0]);
        Check(!ReceivesSocial(link, _ => true, 300),
            "a vendor buy before character selection is dropped instead of running as PID 0");

        var pending = _characterStore.Create(owner, "PendingDelete", 1, new Custom());
        _characterStore.ScheduleDelete(pending.Pid, 60);
        Game.OnHandlePacket(link.Client, [14, .. BitConverter.GetBytes(pending.Pid), 0]);
        Check(link.Client.Player == null,
            "a character queued for deletion cannot be selected (client gates on fDelRemainTime == 0)");

        _characterStore.Get(pending.Pid)!.DeleteAtUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 1;
        _playerManager.SetOnline(pending.Pid, link.Client);
        _characterManager.SendPlayerList(link.Client);
        bool keptWhileOnline = _characterStore.Get(pending.Pid) != null;
        _playerManager.SetOffline(pending.Pid);
        _characterManager.SendPlayerList(link.Client);
        Check(keptWhileOnline && _characterStore.Get(pending.Pid) == null,
            "the elapsed-deletion sweep skips a character that is online and deletes it once offline");

        Check(_characterStore.TryCreate(owner, new string('a', 17), 1, new Custom(), out _) == CharacterStore.CreateWrongName
            && _characterStore.TryCreate(owner, "Ed" + (char)0x200B + "n", 1, new Custom(), out _) == CharacterStore.CreateWrongName
            && _characterStore.TryCreate(owner, "eden_gm", 1, new Custom(), out _) == CharacterStore.CreateWrongName
            && _characterStore.TryCreate(owner, "Name16Characters", 1, new Custom(), out _) == CharacterStore.CreateOk,
            "character names refuse overlong, invisible-character and staff-word names");
    }

    private void AgentLoginAbuseLimits()
    {
        string? previousGmLogins = Environment.GetEnvironmentVariable("HUXLEY_GM_LOGINS");
        Environment.SetEnvironmentVariable("HUXLEY_GM_LOGINS", "gmone,reservedgm");
        var reserved = _accountStore.Register("ReservedGM", "secret", out _);
        Environment.SetEnvironmentVariable("HUXLEY_GM_LOGINS", previousGmLogins);
        Check(reserved == AccountStore.RegistrationResult.Duplicate,
            "a login listed in HUXLEY_GM_LOGINS cannot be registered through op6 NewMember");

        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var peer = new System.Net.Sockets.TcpClient();
        peer.Connect(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
        var accepted = listener.AcceptTcpClient();
        listener.Stop();
        using var agent = new SocialLink(new Client(958830001,
            new Server(0, HandlerType.Agent, static (_, _) => { }, _loggerFactory), accepted), peer);
        peer.ReceiveTimeout = 3000;
        _ = ReceiveSocial(agent); // constructor AgentVersion

        byte[] Wire(byte op, string id, string password)
        {
            using var writer = new PacketWriter();
            writer.Write(op); writer.Write(id); writer.Write(password);
            return writer.Buffer;
        }
        byte[] Ask(byte op, string id, string password)
        {
            Agent.OnHandlePacket(agent.Client, Wire(op, id, password));
            byte[] answer = ReceiveSocial(agent);
            while (answer[0] != (op == 6 ? 6 : 7)) answer = ReceiveSocial(agent);
            return answer;
        }

        Check(_accountStore.TryRegister("throttlelogin", "right", out _), "throttle fixture account registers");
        for (int i = 0; i < AccountSessionStore.MaxLoginFailuresPerLogin; i++)
            _ = Ask(7, "throttlelogin", "wrong");
        Check(Ask(7, "throttlelogin", "right")[1] == (byte)AccountStore.LoginResult.Error,
            "after repeated wrong passwords from one address, logins answer LOGIN_ERROR without checking the password");

        byte last = 0;
        for (int i = 0; i <= AccountSessionStore.MaxRegistrations; i++)
            last = Ask(6, $"throttlereg{i}", "secret")[1];
        Check(last == (byte)AccountStore.RegistrationResult.Error,
            "op6 NewMember creates a bounded number of accounts per address");
    }

    private void PatchPerAddressLimit()
    {
        string root = Path.Combine(Path.GetTempPath(), $"huxley-patch-limit-{Environment.ProcessId}");
        Directory.CreateDirectory(root);
        using var server = new PatchServer(root, [], Path.Combine(root, "version.txt"),
            _loggerFactory.CreateLogger<PatchServer>()) { IoTimeoutMilliseconds = 5000 };
        var idle = new List<System.Net.Sockets.TcpClient>();
        try
        {
            Check(server.Start(0), "patch server starts on an ephemeral port");
            string Get()
            {
                using var http = new System.Net.Sockets.TcpClient("127.0.0.1", server.Port) { ReceiveTimeout = 3000 };
                var stream = http.GetStream();
                stream.Write(System.Text.Encoding.ASCII.GetBytes($"GET {PatchServer.PatchDir}LauncherVersion.txt HTTP/1.0\r\n\r\n"));
                using var body = new MemoryStream();
                try { stream.CopyTo(body); } catch (IOException) { }
                return System.Text.Encoding.ASCII.GetString(body.ToArray());
            }
            for (int i = 0; i < PatchServer.MaxConnectionsPerAddress; i++)
                idle.Add(new System.Net.Sockets.TcpClient("127.0.0.1", server.Port));
            Thread.Sleep(300);
            string busy = Get();
            foreach (var client in idle) client.Dispose();
            idle.Clear();
            Thread.Sleep(300);
            string served = Get();
            var started = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            Check(busy.StartsWith("HTTP/1.0 503") && served.StartsWith("HTTP/1.0 200")
                && PatchServer.BodyDeadline(started, 4 << 20) == started.AddSeconds(30 + 512),
                "one address holding its patch connection quota gets 503 at once; others are still served, and slow bodies time out");
        }
        finally
        {
            foreach (var client in idle) client.Dispose();
            server.Dispose();
            Directory.Delete(root, recursive: true);
        }
    }
}

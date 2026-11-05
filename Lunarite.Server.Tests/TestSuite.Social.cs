using System.Buffers.Binary;
using System.Net;

using Lunarite.Handlers;
using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Outgoing;
using Lunarite.World;

internal sealed partial class TestSuite
{
    private sealed record SocialLink(Client Client, System.Net.Sockets.TcpClient Peer) : IDisposable
    {
        public void Dispose() { Client.Dispose(); Peer.Dispose(); }
    }

    private SocialLink OpenSocialGameSocket(int id)
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var peer = new System.Net.Sockets.TcpClient();
        peer.Connect(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
        var accepted = listener.AcceptTcpClient();
        listener.Stop();
        var client = new Client(id, new Server(0, HandlerType.Game, static (_, _) => { }, _loggerFactory), accepted);
        peer.ReceiveTimeout = 3000;
        var link = new SocialLink(client, peer);
        _ = ReceiveSocial(link); // constructor GameVersion
        return link;
    }

    private static byte[] ReceiveSocial(SocialLink link)
    {
        var stream = link.Peer.GetStream();
        Span<byte> header = stackalloc byte[2];
        stream.ReadExactly(header);
        var payload = new byte[BinaryPrimitives.ReadUInt16LittleEndian(header) - 2];
        stream.ReadExactly(payload);
        for (int i = 0; i < payload.Length; i++) payload[i] = link.Client.DecryptTable[0, payload[i]];
        return payload;
    }

    // Reads until a packet matches or the link goes quiet.
    private static bool ReceivesSocial(SocialLink link, Func<byte[], bool> match, int quietMs = 500)
    {
        int timeout = link.Peer.ReceiveTimeout;
        link.Peer.ReceiveTimeout = quietMs;
        try
        {
            for (int i = 0; i < 32; i++)
                if (match(ReceiveSocial(link))) return true;
        }
        catch (IOException) { }
        finally { link.Peer.ReceiveTimeout = timeout; }
        return false;
    }

    private static byte[] ChatWire(byte type, string to, string message)
    {
        using var writer = new PacketWriter();
        writer.Write((byte)21); writer.Write(type); writer.Write(to); writer.Write(message);
        return writer.Buffer;
    }

    private static void SendSocialChat(SocialLink link, byte type, string to, string message)
    {
        lock (link.Client.Session.ChatSentAt) link.Client.Session.ChatSentAt.Clear();
        Game.OnHandlePacket(link.Client, ChatWire(type, to, message));
    }

    // Whisper and squad chat report unreachable targets through op116 MESSAGE_CHATBOX_ENUM
    // instead of echoing a line nobody receives.
    private void ChatFailuresReportToSender()
    {
        var sender = _characterStore.Create(939870001, "SocialSender", 1, new Custom());
        var friend = _characterStore.Create(939870002, "SocialFriend", 1, new Custom());
        var enemy = _characterStore.Create(939870003, "SocialEnemy", 11, new Custom());
        using var senderLink = OpenSocialGameSocket(939870001);
        using var friendLink = OpenSocialGameSocket(939870002);
        using var enemyLink = OpenSocialGameSocket(939870003);
        foreach (var (character, link) in new[] { (sender, senderLink), (friend, friendLink), (enemy, enemyLink) })
        {
            _playerManager.SetOnline(character.Pid, link.Client);
            link.Client.Player = _playerManager.Get(character.Pid);
        }
        try
        {
            byte[] Failure(byte message) => ChatManager.BuildChatFailure(message).Serialize();
            Check(Failure(ChatManager.NotFoundPlayerMessage).SequenceEqual(new byte[] { 116, 3, 3 })
                && Failure(ChatManager.NotRegSquadMessage).SequenceEqual(new byte[] { 116, 3, 8 })
                && Failure(ChatManager.BlockPlayerMessage).SequenceEqual(new byte[] { 116, 3, 11 })
                && Failure(ChatManager.WhisperDiffTribeMessage).SequenceEqual(new byte[] { 116, 3, 19 }),
                "chat failures use op116 MESSAGE_CHATBOX_ENUM with the client ESubSystemMessage index");

            SendSocialChat(senderLink, ChatType.WhisperSend, "NobodyByThisName", "hi");
            Check(ReceiveSocial(senderLink).SequenceEqual(Failure(ChatManager.NotFoundPlayerMessage)),
                "whisper to an unknown name reports NotFoundPlayer instead of echoing");

            SendSocialChat(senderLink, ChatType.WhisperSend, enemy.Name, "hi");
            Check(ReceiveSocial(senderLink).SequenceEqual(Failure(ChatManager.WhisperDiffTribeMessage))
                && !ReceivesSocial(enemyLink, packet => packet[0] == 25),
                "whisper to the other faction reports Whisper_DiffTribe and is not delivered");

            _blockStore.Add(friend.Pid, sender.Pid);
            SendSocialChat(senderLink, ChatType.WhisperSend, friend.Name, "hi");
            Check(ReceiveSocial(senderLink).SequenceEqual(Failure(ChatManager.BlockPlayerMessage)),
                "whisper to a player who blocks the sender reports BlockPlayer");
            _blockStore.Delete(friend.Pid, sender.Pid);

            _playerManager.SetOffline(friend.Pid);
            SendSocialChat(senderLink, ChatType.WhisperReply, friend.Name, "hi");
            Check(ReceiveSocial(senderLink).SequenceEqual(Failure(ChatManager.NotFoundPlayerMessage)),
                "whisper reply to an offline character reports NotFoundPlayer");
            _playerManager.SetOnline(friend.Pid, friendLink.Client);

            SendSocialChat(senderLink, ChatType.WhisperSend, friend.Name, "hi");
            Check(ReceiveSocial(senderLink).SequenceEqual(new Chat
                    { Type = ChatType.WhisperSend, From = sender.Name, To = friend.Name, Message = "hi" }.Serialize())
                && ReceiveSocial(friendLink).SequenceEqual(new Chat
                    { Type = ChatType.WhisperRecv, From = sender.Name, To = friend.Name, Message = "hi" }.Serialize()),
                "deliverable same-faction whisper still echoes and reaches the target");

            SendSocialChat(senderLink, ChatType.SquadSend, "", "hi");
            Check(ReceiveSocial(senderLink).SequenceEqual(Failure(ChatManager.NotRegSquadMessage)),
                "squad chat without a squad reports NotRegSquad");
        }
        finally
        {
            foreach (var character in new[] { sender, friend, enemy }) _playerManager.SetOffline(character.Pid);
        }
    }

    // op120 keeps the Game link open, so returning to the roster must log the character out.
    private void CharacterMenuReleasesCharacter()
    {
        var leaver = _characterStore.Create(939871001, "RosterLeaver", 1, new Custom());
        var mate = _characterStore.Create(939871002, "RosterMate", 1, new Custom());
        using var leaverLink = OpenSocialGameSocket(939871001);
        using var mateLink = OpenSocialGameSocket(939871002);
        leaverLink.Client.Session.AccountId = 939871001;
        foreach (var (character, link) in new[] { (leaver, leaverLink), (mate, mateLink) })
        {
            ResetCharacter(character.Pid);
            _playerManager.SetOnline(character.Pid, link.Client);
            link.Client.Player = _playerManager.Get(character.Pid);
            _playerManager.Get(character.Pid).MarkSpawned();
            _playerManager.Get(character.Pid).CityMap = 0;
        }
        const int squadKey = 939871;
        _squadManager.SetSquad(squadKey, leaver.Pid, [leaver.Pid, mate.Pid]);
        try
        {
            byte[] TradeRequest(byte action, int target)
            {
                var wire = new byte[6]; wire[0] = 78; wire[1] = action;
                BinaryPrimitives.WriteInt32LittleEndian(wire.AsSpan(2), target);
                return wire;
            }
            Game.OnHandlePacket(leaverLink.Client, TradeRequest(0, mate.Pid));
            Game.OnHandlePacket(mateLink.Client, TradeRequest(1, leaver.Pid));
            Check(ReceivesSocial(leaverLink, packet => packet[0] == 85 && packet[1] == TradeAction.Accept),
                "roster-exit fixture opens a trade between the two characters");

            Game.OnHandlePacket(leaverLink.Client, new byte[] { 120 });
            var cancel = new Trade { Action = TradeAction.Cancel, Pid = leaver.Pid }.Serialize();
            Check(leaverLink.Client.Player == null && !_playerManager.IsOnline(leaver.Pid)
                && _playerManager.IsOnline(mate.Pid),
                "returning to character select takes the character offline and unbinds the link");
            Check(_squadManager.SquadMembersOf(leaver.Pid).Count == 0
                && !_squadManager.SquadMembersOf(mate.Pid).Contains(leaver.Pid),
                "returning to character select leaves the squad");
            Check(ReceivesSocial(mateLink, packet => packet.SequenceEqual(cancel)),
                "returning to character select cancels the open trade for the partner");
        }
        finally
        {
            _squadManager.RemoveSquad(squadKey);
            _playerManager.SetOffline(leaver.Pid);
            _playerManager.SetOffline(mate.Pid);
        }
    }
}

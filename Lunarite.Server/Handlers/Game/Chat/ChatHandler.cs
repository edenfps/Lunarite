using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class ChatHandler
{
    private static CityManager _cityManager = null!;
    private static GmCommands _gmCommands = null!;
    private static SimulationManager _simulationManager = null!;
    private static SquadManager _squadManager = null!;
    private static ILogger _logger = null!;
    private static BlockStore _blockStore = null!;
    private static CharacterStore _characterStore = null!;
    private static ClanStore _clanStore = null!;
    private static PlayerManager _playerManager = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(ChatHandler));

        _blockStore = serviceProvider.GetRequiredService<BlockStore>();
        _characterStore = serviceProvider.GetRequiredService<CharacterStore>();
        _clanStore = serviceProvider.GetRequiredService<ClanStore>();
        _playerManager = serviceProvider.GetRequiredService<PlayerManager>();
        _cityManager = serviceProvider.GetRequiredService<CityManager>();
        _gmCommands = serviceProvider.GetRequiredService<GmCommands>();
        _simulationManager = serviceProvider.GetRequiredService<SimulationManager>();
        _squadManager = serviceProvider.GetRequiredService<SquadManager>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        // Client op21: bss = send type, recipient, text. Server op25 adds sender name.
        if (!reader.TryRead(out byte type) || !reader.TryRead(out string? to)
            || !reader.TryRead(out string? message) || reader.RemainingLength != 0) return false;
        int senderPid = Game.PidOf(client);
        string from = _characterStore.Get(senderPid)?.Name ?? "";
        if (from.Length == 0 || string.IsNullOrEmpty(message)) return true;

        byte receiveType = ChatManager.ReceiveChatType(type);
        if (receiveType == byte.MaxValue)
        {
            _logger.LogWarning("[Chat] rejected non-send chat type {0} from PID {1}.", type, senderPid);
            return true;
        }
        if (CityManager.IsUnstuckCommand(message)) { _cityManager.Unstuck(client, senderPid); return true; }
        if (message.StartsWith('!') && _gmCommands.IsGm(client) && _gmCommands.RunGmCommand(client, from, message)) return true;

        // Exported AllowTextMessage permits five messages in ten seconds, then blocks for 59.
        // The same limit applies here, reported through the native callback.
        var session = client.Session;
        long now = Environment.TickCount64;
        lock (session.ChatSentAt)
        {
            if (now < session.ChatBlockedUntil) return true;
            while (session.ChatSentAt.Count != 0 && now - session.ChatSentAt.Peek() >= 10_000)
                session.ChatSentAt.Dequeue();
            if (session.ChatSentAt.Count >= 5)
            {
                session.ChatSentAt.Clear();
                session.ChatBlockedUntil = now + 59_000;
                client.SendPacket(new ChatBlocked());
                return true;
            }
            session.ChatSentAt.Enqueue(now);
        }

        var incoming = new Chat { Type = receiveType, From = from, To = to ?? "", Message = message };
        var recipients = new HashSet<int>();
        byte failure = 0;
        if (type is 4 or 6) // whisper / whisper reply
        {
            var target = _characterStore.All().FirstOrDefault(c =>
                string.Equals(c.Name, to, StringComparison.OrdinalIgnoreCase));
            failure = ChatManager.WhisperFailure(target != null && Game.TryGetClient(target.Pid, out _),
                _characterStore.Get(senderPid)?.Index ?? 0, target?.Index ?? 0,
                target != null && _blockStore.List(target.Pid).Contains(senderPid));
            if (failure == 0) recipients.Add(target!.Pid);
        }
        else if (type == 7) // squad
        {
            _squadManager.AddSquadChatRecipients(senderPid, recipients);
            if (recipients.Count == 0) failure = ChatManager.NotRegSquadMessage;
        }
        else if (type == 9) // clan
        {
            var clan = _clanStore.ForPid(senderPid);
            if (clan != null) recipients.UnionWith(clan.Members.Select(x => x.Pid));
        }
        else if (type == 21) // Simulation Center team
        {
            _simulationManager.AddTeamChatRecipients(senderPid, recipients);
        }
        else if (type == 2) // shout: everyone online in the sender's faction (one city per faction)
        {
            byte senderIndex = _characterStore.Get(senderPid)?.Index ?? 0;
            foreach (int pid in _playerManager.OnlinePids)
                if (ChatManager.ShoutReaches(senderIndex, _characterStore.Get(pid)?.Index ?? 0)) recipients.Add(pid);
        }
        else // normal, social action, trade: current city/quest presence
        {
            foreach (var pair in _playerManager.Online)
                if (_cityManager.SharesPresence(client, pair.Value)) recipients.Add(pair.Key);
        }

        if (failure != 0)
        {
            client.SendPacket(ChatManager.BuildChatFailure(failure));
            return true;
        }
        client.SendPacket(new Chat { Type = type, From = from, To = to ?? "", Message = message });

        recipients.Remove(senderPid);
        foreach (int recipientPid in recipients)
            if (!_blockStore.List(recipientPid).Contains(senderPid)
                && Game.TryGetClient(recipientPid, out var targetClient))
                targetClient.SendPacket(incoming);
        return true;
    }
}

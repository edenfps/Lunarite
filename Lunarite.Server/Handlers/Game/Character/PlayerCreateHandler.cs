using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.Packets.Incoming;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class PlayerCreateHandler
{
    private static MultiStoreTransaction _multiStoreTransaction = null!;
    private static ILogger _logger = null!;
    private static CharacterStore _characterStore = null!;
    private static LicenseStore _licenseStore = null!;
    private static PlayerStore _playerStore = null!;
    private static QuestStore _questStore = null!;
    private static AccountStore _accountStore = null!;
    private static ItemManager _itemManager = null!;

    // Customize Character (LunariteMenus on the character select screen) reuses this request: the name is the
    // character's own name behind this marker, and the reply is that character with its new appearance (same PID),
    // which the menu swaps into its list. So no new name may start with '~'.
    public const char CustomizeMarker = '~';

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(PlayerCreateHandler));

        _characterStore = serviceProvider.GetRequiredService<CharacterStore>();
        _licenseStore = serviceProvider.GetRequiredService<LicenseStore>();
        _playerStore = serviceProvider.GetRequiredService<PlayerStore>();
        _questStore = serviceProvider.GetRequiredService<QuestStore>();
        _multiStoreTransaction = serviceProvider.GetRequiredService<MultiStoreTransaction>();
        _accountStore = serviceProvider.GetRequiredService<AccountStore>();
        _itemManager = serviceProvider.GetRequiredService<ItemManager>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!PlayerCreateRequest.TryDeserialize(ref reader, out var packet))
        {
            _logger.LogWarning("Failed to deserialize {0}.", nameof(PlayerCreateRequest));
            return false;
        }

        _logger.LogDebug("Received {0} packet. ( {1} )", nameof(PlayerCreateRequest), packet);

        if (client.Session.AccountId is not int userId) return true;
        if (packet.Name is { Length: > 1 } marked && marked[0] == CustomizeMarker)
        {
            var edited = _characterStore.TrySetCustom(userId, marked[1..], packet.Index, packet.Custom);
            client.SendPacket(edited == null ? new CreatePlayer { Answer = CharacterStore.CreateError } : Reply(edited, _itemManager.EquippedArmorPreview(edited.Pid)));
            return true;
        }
        byte createResult = CharacterStore.CreateError;
        CharacterStore.Character? character = null;
        _multiStoreTransaction.Run(() =>
        {
            createResult = _characterStore.TryCreate(userId, packet.Name ?? "", packet.Index,
                packet.Custom, out character, _accountStore.LoginFor(userId));
            if (createResult != CharacterStore.CreateOk || character == null) return;
            _playerStore.ResetCharacter(character.Pid);   // level 1, empty inventory
            _licenseStore.Reset(character.Pid);
            _questStore.Reset(character.Pid);
        });
        if (createResult != CharacterStore.CreateOk || character == null)
        {
            client.SendPacket(new CreatePlayer { Answer = createResult });
            return true;
        }
        client.SendPacket(Reply(character, ItemManager.InitialArmorPreview()));
        return true;
    }

    private static CreatePlayer Reply(CharacterStore.Character character, Packets.Common.GenericItemBase[] armor)
    {
        var progress = _playerStore.GetProgress(character.Pid);
        return new CreatePlayer
        {
            Answer = 0,
            Pid = character.Pid,
            Name = character.Name,
            // Species TypeIndex (client struct +36); selects the pawn model. Must match PlayerInfo.Index.
            Index = character.Index,
            // HUXLEY_STARTING_MONEY is a long; the wire field is the client's int nMoney.
            Money = (int)Math.Clamp(progress.Money, 0, PlayerStore.MaxMoney),
            Level = (short)progress.Level,
            Style = character.Style,
            Rank = progress.Rank,
            QT = (int)Math.Min(int.MaxValue, progress.QT),
            Custom = character.Custom,
            // Native OnCreatePlayer always consumes five 12-byte GenericItemBase records in
            // ARMORPART order. Tutorial gear has no helmet, so slot 0 is empty.
            ArmorList = armor
        };
    }
}

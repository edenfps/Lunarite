using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Incoming;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class ParameterHandler
{
    private static MultiStoreTransaction _multiStoreTransaction = null!;
    private static ItemManager _itemManager = null!;
    private static QuestManager _questManager = null!;
    private static ILogger _logger = null!;
    private static CharacterStore _characterStore = null!;
    private static LicenseStore _licenseStore = null!;
    private static PlayerStore _playerStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(ParameterHandler));

        _characterStore = serviceProvider.GetRequiredService<CharacterStore>();
        _licenseStore = serviceProvider.GetRequiredService<LicenseStore>();
        _playerStore = serviceProvider.GetRequiredService<PlayerStore>();
        _itemManager = serviceProvider.GetRequiredService<ItemManager>();
        _questManager = serviceProvider.GetRequiredService<QuestManager>();
        _multiStoreTransaction = serviceProvider.GetRequiredService<MultiStoreTransaction>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!ParameterRequest.TryDeserialize(ref reader, out var packet))
        {
            _logger.LogWarning("Failed to deserialize {0}.", nameof(ParameterRequest));
            return false;
        }

        _logger.LogDebug("Received {0} packet. ( {1} )", nameof(ParameterRequest), packet);

        // PARAMETER_STYLE = 6; CombatStyle 0=None 1=Enforcer 2=Avenger 3=Phantom.
        // HuxleyStyleConfirmMenu only calls SendParameter(STYLE); the client sets byStyle when
        // the server echoes op16. PlayerInfo.CharInfo.Style carries it on later logins.
        if (packet.Param == Parameter.PARAMETER_STYLE)
        {
            int pid = Game.PidOf(client);
            if (pid <= 0 || _characterStore.Get(pid) is null)
            {
                _logger.LogWarning("Ignoring combat-style change for an unselected character (client {0}).", client.Id);
                return true;
            }

            // Only styles 1-3 exist; reject anything that would wrap in the byte cast.
            if (packet.Value is < 1 or > 3)
            {
                _logger.LogWarning("Ignoring invalid combat style {0} for PID {1}.", packet.Value, pid);
                return true;
            }

            byte style = checked((byte)packet.Value);
            var ids = CharacterManager.StyleWeapons(style);
            List<PlayerStore.GenericItem> weaponsBefore = [];
            bool styleSet = false;
            _multiStoreTransaction.Run(() =>
            {
                if (!_characterStore.TrySetInitialStyle(pid, style)) return;
                // The weapon swap is persisted together with the one-time style choice.
                weaponsBefore = _playerStore.GetInventory(pid, () => _itemManager.BuildInitialInventory(pid))
                    .Where(item => item.InventoryIndex == InventoryType.Weapon).Select(ItemManager.CloneInventoryItem).ToList();
                _playerStore.ReplaceInventorySlots(pid, InventoryType.Weapon, ids);
                styleSet = true;
            });
            if (!styleSet)
            {
                byte authoritativeStyle = _characterStore.Get(pid)?.Style ?? 0;
                _logger.LogWarning("Ignoring repeated combat-style choice {0} for PID {1}; authoritative style is {2}.",
                    style, pid, authoritativeStyle);
                if (authoritativeStyle != 0)
                    client.SendPacket(new Parameter
                        { Param = Parameter.PARAMETER_STYLE, Value = authoritativeStyle });
                return true;
            }

            // Native op217 replaces a slot with a full GenericItem in one step, keeping durability,
            // state, timers, tune-up and socket fields.
            var weaponsAfter = _playerStore.GetInventory(pid, () => _itemManager.BuildInitialInventory(pid))
                .Where(item => item.InventoryIndex == InventoryType.Weapon).ToList();
            foreach (var update in ItemManager.BuildInventorySlotChanges(weaponsBefore, weaponsAfter, InventoryType.Weapon))
                client.SendPacket(update);

            client.SendPacket(new Parameter { Param = Parameter.PARAMETER_STYLE, Value = packet.Value });
            client.SendPacket(new SelectStyle
            {
                SkillSlots = _playerStore.GetSkillSlots(pid),
                Licenses = _licenseStore.Effective(pid).ToList(),
                Items = _playerStore.GetInventory(pid, () => _itemManager.BuildInitialInventory(pid))
                    .Where(x => x.InventoryIndex < 7)
                    .Select(x => new SelectStyle.Item(x.InventoryIndex, ItemManager.SerializeGenericItem(x)))
                    .ToList()
            });
            _questManager.SendQuestAvailabilityUpdate(client, pid);
            _logger.LogInformation("Combat style set to {0} for PID {1}; weapons -> [{2}].", style, pid, string.Join(",", ids));
        }

        return true;
    }
}

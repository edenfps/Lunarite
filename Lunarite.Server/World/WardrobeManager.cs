using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Outgoing;
using Lunarite.Resources;
using Lunarite.Storage;

using Microsoft.Extensions.Logging;

namespace Lunarite.World;

// Wardrobe (transmog). Rules, the "/wardrobe" chat protocol of the LunariteMenus Wardrobe menu,
// and the item IDs other players' clients get for a character's armor.
//
// Protocol (stock chat drops received lines that start with "/", so none of this shows in the chat box):
//   client: /wardrobe list | /wardrobe set <part> <itemId> | /wardrobe hide | /wardrobe reset <part>
//   server: /wardrobe begin | /wardrobe looks <h> <u> <l> <g> <b> | /wardrobe items <part> <id>,<id>,...
//           | /wardrobe end | /wardrobe ok <part> <look> | /wardrobe error <text>
// A look is 0 (own armor), -1 (hidden helmet) or an item ID. Parts are ARMOR cells 0-4.
// Cross-race looks: an item ID with OtherRace set shows the item's mesh for the
// other race of the wearer's gender (an Alternative woman in the Sapiens version, a Sapiens woman in the Alternative one,
// Union and Atreides men likewise). Only the wearer's own LunariteMenus client draws it; other players' clients get
// the plain item ID and show the wearer's own race version.
public sealed class WardrobeManager
{
    private const string Prefix = "/wardrobe";
    private const int IdsPerLine = 24;
    public const int OtherRace = 1 << 30;
    // Off (user, 2026-11-11: "keep Sapiens / Alternative clothing separate"). Stored other-race looks show as the item's
    // own-race version. The client support stays in LunariteMenus.
    public static bool CrossRaceLooks = false;

    // Head items whose other-race mesh clips too much on this wearer (head-item audit: needs over 6 % size or
    // never fits). Key: wearer species TypeIndex; value: item IDs (from the MeshLinks resource names) not offered cross-race.
    private static readonly Dictionary<byte, HashSet<int>> SameRaceOnly = new()
    {
        // ATN-F wearer, SYN-F mesh: SK_SYN_F_C_L_002_GL
        [16] = [225504, 225506, 225507, 225512, 225514, 225516, 225517, 225522, 225524, 225526, 225527],
        // SYN-F wearer, ATN-F meshes: A_H_001, A_H_002, A_L_002, A_M_001, B_M_001 (_MK), C_M_002_HM, D_M_001_MK
        [6] = [211501, 211502, 211503, 211504, 211505, 211506, 217001, 217502, 217503, 217504, 217505, 217506, 217511,
            217512, 217513, 217514, 217515, 217516, 217521, 217522, 217523, 217524, 217525, 217526, 218501, 218502, 218503,
            218504, 226503, 226513, 226523, 227504, 227506, 227507, 227512, 227514, 227516, 227517, 227522, 227524, 227526,
            227527, 231501, 231502, 231503, 231504, 231505, 231506, 231508, 231509, 231510, 231511, 231512, 231513, 232501,
            232502, 232503, 232504, 232505, 232506, 232508, 232509, 232510, 232511, 232512, 232513, 237513, 237514, 237521,
            237522, 237523, 237524, 238501, 238502, 238503, 238504, 238505, 238506, 238507, 239501, 239502, 239503, 239504,
            239505, 239506, 239507, 240501, 241501, 246001, 247001, 248001],
        // ATR-M wearer, ONE-M meshes: A_H_002_HM, A_L_002_HM, C_L_002_GL, C_M_001_HM
        [11] = [221001, 221002, 221003, 221004, 221005, 221006, 221008, 221009, 221010, 221011, 221012, 224001, 224503,
            224505, 224511, 224513, 224515, 224521, 224523, 224525, 225504, 225506, 225507, 225512, 225514, 225516, 225517,
            225522, 225524, 225526, 225527, 228001, 228002, 228003, 228004, 228005, 243001, 245001],
    };

    public static int ItemOf(int look) => look > 0 ? look & ~OtherRace : look;
    public static bool IsOtherRace(int look) => look > 0 && (look & OtherRace) != 0;

    private readonly ILogger _logger;
    private readonly WardrobeStore _wardrobeStore;
    private readonly PlayerStore _playerStore;
    private readonly CharacterStore _characterStore;
    private readonly ItemCatalog _itemCatalog;
    private readonly CityManager _cityManager;

    public WardrobeManager(ILogger<WardrobeManager> logger, WardrobeStore wardrobeStore, PlayerStore playerStore,
        CharacterStore characterStore, ItemCatalog itemCatalog, CityManager cityManager)
    {
        _logger = logger;
        _wardrobeStore = wardrobeStore;
        _playerStore = playerStore;
        _characterStore = characterStore;
        _itemCatalog = itemCatalog;
        _cityManager = cityManager;
    }

    // ARMOR inventory cell of an armor item, or -1.
    public static int PartOf(ItemCatalog.Item? data) => data?.ArmorPart.ToLowerInvariant() switch
    {
        "pos_helmet" => 0, "pos_upbody" => 1, "pos_lowbody" => 2, "pos_gloves" => 3, "pos_boots" => 4, _ => -1
    };

    public int PartOf(int itemId) => itemId > 0 ? PartOf(_itemCatalog.Get(itemId)) : -1;

    private string? KeyFor(int pid)
    {
        var character = _characterStore.Get(pid);
        return character == null || character.Index == 0 ? null : WardrobeStore.Key(character.OwnerUserId, character.Index);
    }

    // Collects every armor piece the character holds now. Returns how many looks were new.
    public int CollectFrom(int pid)
    {
        string? key = KeyFor(pid);
        if (key == null) return 0;
        var armor = _playerStore.ItemIdsOf(pid).Where(id => PartOf(id) >= 0).Distinct().ToList();
        return armor.Count == 0 ? 0 : _wardrobeStore.Collect(key, armor);
    }

    public void CollectOnline(IEnumerable<int> pids)
    {
        foreach (int pid in pids)
        {
            try { CollectFrom(pid); }
            catch (Exception ex) { _logger.LogWarning("[Wardrobe] collect PID {0}: {1}", pid, ex.Message); }
        }
    }

    // One time: every character's current inventory, plus the armor each character held in any hourly backup
    // (wardrobe-history.json: {"first_seen": {"<pid>": {"<itemId>": "<snapshot>"}}}, made 2026-11-11 on the VPS).
    public void BackfillOnce(string? historyPath)
    {
        if (_wardrobeStore.Backfilled) return;
        int pids = 0, added = 0;
        foreach (int pid in _playerStore.InventoryPids())
        {
            added += CollectFrom(pid);
            pids++;
        }
        if (!string.IsNullOrEmpty(historyPath) && File.Exists(historyPath))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(historyPath));
                foreach (var entry in doc.RootElement.GetProperty("first_seen").EnumerateObject())
                {
                    if (!int.TryParse(entry.Name, out int pid) || KeyFor(pid) is not { } key) continue;
                    var armor = entry.Value.EnumerateObject()
                        .Select(x => int.TryParse(x.Name, out int id) ? id : 0)
                        .Where(id => PartOf(id) >= 0).ToList();
                    added += _wardrobeStore.Collect(key, armor);
                }
                _logger.LogInformation("[Wardrobe] backfill read history {0}.", historyPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[Wardrobe] backfill history {0} failed: {1}", historyPath, ex.Message);
                return; // retried on the next start
            }
        }
        else
        {
            _logger.LogWarning("[Wardrobe] no backfill history at '{0}'; current inventories only.", historyPath ?? "");
        }
        _wardrobeStore.MarkBackfilled();
        _logger.LogInformation("[Wardrobe] backfill done: {0} inventories, {1} looks collected.", pids, added);
    }

    // Looks this character may pick for a slot: anything collected on the account by a character of the same gender, any
    // combat style (user, 2026-11-11). Every armor item has all four body meshes.
    // Each item comes twice: the wearer's own race version, then (OtherRace set) the other race's version, unless the
    // head-item audit found that one clips on this wearer.
    public List<int> Available(int pid, int part)
    {
        string? key = KeyFor(pid);
        if (key == null) return [];
        byte species = _characterStore.Get(pid)?.Index ?? 0;
        var result = new List<int>();
        foreach (int id in _wardrobeStore.Collection(key))
        {
            if (_itemCatalog.Get(id) is not { } data || PartOf(data) != part) continue;
            result.Add(id);
            if (CrossRaceLooks && CrossAllowed(species, id)) result.Add(id | OtherRace);
        }
        return result;
    }

    private static bool CrossAllowed(byte species, int itemId) =>
        !(SameRaceOnly.TryGetValue(species, out var blocked) && blocked.Contains(itemId));

    private bool IsValidLook(int pid, int part, int look)
    {
        if (look == WardrobeStore.OwnArmor) return true;
        if (look == WardrobeStore.HiddenHelmet) return part == 0;
        if (look < 0) return false;
        int item = ItemOf(look);
        if (IsOtherRace(look) && !CrossRaceLooks) return false;
        if (IsOtherRace(look) && !CrossAllowed(_characterStore.Get(pid)?.Index ?? 0, item)) return false;
        return KeyFor(pid) is { } key && _wardrobeStore.Has(key, item)
            && _itemCatalog.Get(item) is { } data && PartOf(data) == part;
    }

    // The item ID other players see in a slot whose equipped piece is realItemId (0 = nothing shown).
    // A look only covers an equipped piece: an empty slot stays empty.
    public int Shown(int pid, int part, int realItemId)
    {
        if (realItemId == 0 || part < 0 || part >= WardrobeStore.PartCount) return realItemId;
        int look = CrossRaceLooks ? _wardrobeStore.Looks(pid)[part] : ItemOf(_wardrobeStore.Looks(pid)[part]);
        if (look == WardrobeStore.OwnArmor || !IsValidLook(pid, part, look)) return realItemId;
        return look == WardrobeStore.HiddenHelmet ? 0 : ItemOf(look);
    }

    // An equipped ARMOR item as other players should get it: the look's ID, or null when hidden.
    public PlayerStore.GenericItem? ShownItem(int pid, PlayerStore.GenericItem item)
    {
        if (item.InventoryIndex != InventoryType.Armor) return item;
        int shown = Shown(pid, item.InventoryCell, item.ItemId);
        if (shown == item.ItemId) return item;
        if (shown == 0) return null;
        return new PlayerStore.GenericItem
        {
            InventoryIndex = item.InventoryIndex, InventoryCell = item.InventoryCell, ItemId = shown,
            Count = item.Count, State = item.State, LimitTime = item.LimitTime, TuneUpOptions = item.TuneUpOptions,
            Durability = item.Durability, Spray = item.Spray, Skill = item.Skill, Skill1 = item.Skill1
        };
    }

    // op80 for other players with the look applied (op68 with 0 when the helmet is hidden).
    public IPacketSerialize ArmorOther(int pid, byte part, PlayerStore.GenericItem? item)
    {
        if (item == null) return new UpdateArmorOther { Pid = pid, Part = part, Item = null };
        var shown = ShownItem(pid, item);
        return shown == null
            ? new EquipArmorOther { Pid = pid, Part = part, ItemId = 0 }
            : new UpdateArmorOther { Pid = pid, Part = part, Item = shown };
    }

    public static bool IsCommand(string message) =>
        message.Equals(Prefix, StringComparison.OrdinalIgnoreCase)
        || message.StartsWith(Prefix + " ", StringComparison.OrdinalIgnoreCase);

    private static void Send(Client client, string line) =>
        client.SendPacket(new Chat { Type = ChatType.NormalRecv, From = "", To = "", Message = line });

    public void HandleCommand(Client client, int pid, string message)
    {
        string[] words = message.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string verb = words.Length > 1 ? words[1].ToLowerInvariant() : "list";
        if (pid <= 0 || KeyFor(pid) == null)
        {
            Send(client, Prefix + " error no character");
            return;
        }
        switch (verb)
        {
            case "list":
                CollectFrom(pid);
                SendList(client, pid);
                return;
            case "set" when words.Length == 4 && int.TryParse(words[2], out int part) && int.TryParse(words[3], out int look):
                SetLook(client, pid, part, look);
                return;
            case "hide":
                SetLook(client, pid, 0, WardrobeStore.HiddenHelmet);
                return;
            case "reset" when words.Length == 3 && int.TryParse(words[2], out int resetPart):
                SetLook(client, pid, resetPart, WardrobeStore.OwnArmor);
                return;
            default:
                Send(client, Prefix + " error bad command");
                return;
        }
    }

    private void SendList(Client client, int pid)
    {
        Send(client, Prefix + " begin");
        Send(client, Prefix + " looks " + string.Join(' ', _wardrobeStore.Looks(pid).Select(x => CrossRaceLooks ? x : ItemOf(x))));
        for (int part = 0; part < WardrobeStore.PartCount; part++)
        {
            var ids = Available(pid, part);
            for (int i = 0; i < ids.Count; i += IdsPerLine)
                Send(client, Prefix + " items " + part + " " + string.Join(',', ids.Skip(i).Take(IdsPerLine)));
        }
        Send(client, Prefix + " end");
    }

    private void SetLook(Client client, int pid, int part, int look)
    {
        if (part < 0 || part >= WardrobeStore.PartCount || !IsValidLook(pid, part, look))
        {
            Send(client, Prefix + " error not collected");
            return;
        }
        _wardrobeStore.SetLook(pid, part, look);
        Send(client, Prefix + " ok " + part + " " + look);
        int real = _playerStore.ItemIdsOfArmorCell(pid, (byte)part);
        _cityManager.BroadcastPresence(client, new EquipArmorOther { Pid = pid, Part = (byte)part, ItemId = Shown(pid, part, real) });
        _logger.LogInformation("[Wardrobe] PID {0} slot {1} look {2}.", pid, part, look);
    }
}

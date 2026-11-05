using System;
using System.Linq;

using Lunarite.Resources;
using Lunarite.Storage;

namespace Lunarite.World;

// Authoritative counterparts of HuxleyPCPlayerInventoryMenu's weapon-equip preflight. Results are
// the exact ENUM_RESULTTYPE_ITEM ordinals consumed by OnEquipWeapon.
public sealed class EquipmentRules
{
    private readonly CharacterStore _characterStore;
    private readonly DurativeItemStore _durativeItemStore;
    private readonly LicenseStore _licenseStore;
    private readonly PlayerStore _playerStore;

    private readonly ServerEvents _serverEvents;

    public EquipmentRules(CharacterStore characterStore, DurativeItemStore durativeItemStore,
        LicenseStore licenseStore, PlayerStore playerStore, ServerEvents serverEvents)
    {
        _characterStore = characterStore;
        _durativeItemStore = durativeItemStore;
        _licenseStore = licenseStore;
        _playerStore = playerStore;
        _serverEvents = serverEvents;
    }

    // Auction's "usable" filter is a server query, so it must apply the same character gates as
    // equip/use without requiring an inventory cell or an open destination slot. The native item
    // result contract proves the shared level, rank, style and weapon-license qualifications.
    public bool IsUsable(int pid, PlayerStore.GenericItem item, ItemCatalog.Item? data)
    {
        if (data == null) return false;
        var progress = _playerStore.GetProgress(pid);
        int equipLevel = data.EquipLevel;
        if (data.WeaponGroup.Length != 0 || data.ArmorPart.Length != 0)
            equipLevel -= (item.TuneUpOptions >> 16) & 0xF;
        if (progress.Level < equipLevel || progress.Rank < data.EquipRank) return false;

        string playerStyle = _characterStore.Get(pid)?.Style switch
        {
            1 => "CombatStyle_Enforcer", 2 => "CombatStyle_Avenger",
            3 => "CombatStyle_Phantom", _ => ""
        };
        if (data.CombatStyle.Length != 0 && data.CombatStyle != "CombatStyle_None"
            && data.CombatStyle != playerStyle)
        {
            if (data.WeaponGroup.Length == 0 || data.EquipLevel >= 30
                || (data.ExclusiveCombatStyle.Length != 0
                    && data.ExclusiveCombatStyle != "CombatStyle_None"
                    && data.ExclusiveCombatStyle != playerStyle))
                return false;
        }

        return data.WeaponGroup.Length == 0 || _licenseStore.HaveWeaponLicense(pid, data);
    }

    public byte ValidateWeapon(int pid, byte weaponCell, byte mainCell,
        PlayerStore.GenericItem? source, ItemCatalog.Item? data)
    {
        if (!IsOpenedWeaponSlot(pid, weaponCell)) return 5;                 // WEAPON_OUTOFBOUND
        if (mainCell >= _playerStore.MainCapacity(pid)) return 6;         // INVEN_OUTOFBOUND
        if (source == null) return 3;                                     // NOTEXIST
        if (data == null || data.WeaponGroup.Length == 0) return 12;       // WRONG_POS
        if (_serverEvents.BlocksPcRoomItem(data)) return 48;                // PCROOM_ITEM
        if (source.Durability <= 0) return 1;                              // ERROR (broken)
        var progress = _playerStore.GetProgress(pid);
        if (progress.Level < GetEquipLevel(data, source.TuneUpOptions)) return 40;
        if (progress.Rank < data.EquipRank) return 41;                     // NOT_ENOUGH_PLAYERRANK

        string playerStyle = _characterStore.Get(pid)?.Style switch
        {
            1 => "CombatStyle_Enforcer", 2 => "CombatStyle_Avenger",
            3 => "CombatStyle_Phantom", _ => ""
        };
        // HuxleyWeaponDataDescriptor only marks a cross-style weapon unqualified when it is
        // S-class, or its concrete weapon family has a non-None ExclusiveCombatStyle CDO default.
        if (data.CombatStyle.Length != 0 && data.CombatStyle != "CombatStyle_None"
            && data.CombatStyle != playerStyle
            && (data.EquipLevel >= 30
                || (data.ExclusiveCombatStyle.Length != 0
                    && data.ExclusiveCombatStyle != "CombatStyle_None"
                    && data.ExclusiveCombatStyle != playerStyle))) return 15; // CLASS
        if (!_licenseStore.HaveWeaponLicense(pid, data)) return 22;          // WEAPON_LICENSE
        return 0;
    }

    // HuxleyPlayerWrapper.IsOpenedWeaponSlot: 0/1 are permanent; 2/3 require an active
    // ECIG_OpenWeaponSlot(9) DurativeItem whose OptionValue equals the requested slot.
    public bool IsOpenedWeaponSlot(int pid, int weaponCell)
    {
        if (weaponCell < 0 || weaponCell > 3) return false;
        if (weaponCell < 2) return true;
        return _durativeItemStore.Active(pid).Any(x => x.CityItemGroup == 9 && x.OptionValue == weaponCell);
    }

    // HuxleyWeaponData.GetEquipLevel: Level is category 4 in every recovered retail weapon-class
    // CDO and TuneUpInfo.Level is -1, so each packed level tune reduces the requirement by one.
    public static int GetEquipLevel(ItemCatalog.Item data, int packedTuneUps) =>
        data.EquipLevel - ((packedTuneUps >> 16) & 0xF);

    public byte ValidateArmor(int pid, byte armorCell, byte mainCell,
        PlayerStore.GenericItem? source, ItemCatalog.Item? data)
    {
        if (armorCell >= 5) return 4;                                     // ARMOR_OUTOFBOUND
        if (mainCell >= _playerStore.MainCapacity(pid)) return 6;         // INVEN_OUTOFBOUND
        if (source == null) return 3;                                    // NOTEXIST
        int expectedCell = data?.ArmorPart.ToLowerInvariant() switch
        {
            "pos_helmet" => 0, "pos_upbody" => 1, "pos_lowbody" => 2,
            "pos_gloves" => 3, "pos_boots" => 4, _ => -1
        };
        if (expectedCell != armorCell) return 12;                         // WRONG_POS
        if (_serverEvents.BlocksPcRoomItem(data)) return 48;               // PCROOM_ITEM
        if (source.Durability <= 0) return 1;                             // ERROR (broken)
        int equipLevel = data!.EquipLevel - ((source.TuneUpOptions >> 16) & 0xF); // data matched an armor part
        var progress = _playerStore.GetProgress(pid);
        if (progress.Level < equipLevel) return 40;                       // NOT_ENOUGH_PLAYERLEVEL
        if (progress.Rank < data.EquipRank) return 41;                    // NOT_ENOUGH_PLAYERRANK
        string playerStyle = _characterStore.Get(pid)?.Style switch
        {
            1 => "CombatStyle_Enforcer", 2 => "CombatStyle_Avenger",
            3 => "CombatStyle_Phantom", _ => ""
        };
        if (data.CombatStyle.Length != 0 && data.CombatStyle != "CombatStyle_None"
            && data.CombatStyle != playerStyle) return 15;                // CLASS
        return 0;
    }

    public byte ValidateBelt(int pid, byte mainCell, PlayerStore.GenericItem? source,
        ItemCatalog.Item? data)
    {
        if (mainCell >= _playerStore.MainCapacity(pid)) return 6;         // INVEN_OUTOFBOUND
        if (source == null) return 3;                                    // NOTEXIST
        if (data == null || !data.ClassName.Contains("HuxleyBattleItemData",
                StringComparison.Ordinal)) return 12;                    // WRONG_POS
        if (_serverEvents.BlocksPcRoomItem(data)) return 48;              // PCROOM_ITEM
        var progress = _playerStore.GetProgress(pid);
        if (progress.Level < data.EquipLevel) return 40;
        if (progress.Rank < data.EquipRank) return 41;
        return 0;
    }

    public byte ValidateSkill(int pid, byte armorCell, byte socket, byte mainCell, byte skillCell,
        PlayerStore.GenericItem? armor, PlayerStore.GenericItem? source,
        ItemCatalog.Item? armorData, ItemCatalog.Item? skillData)
    {
        if (armorCell >= 5) return 4;                                     // ARMOR_OUTOFBOUND
        if (mainCell >= _playerStore.MainCapacity(pid)) return 6;         // INVEN_OUTOFBOUND
        if (socket >= 2 || skillCell >= 5) return 23;                     // WRONG_PARAMETER
        // HuxleyPCPlayerInventoryMenu sends the skill-panel button whose nSkillSlot is the part.
        if (_playerStore.GetProgress(pid).SkillSlot[skillCell] != armorCell) return 23;
        if (armor == null || source == null) return 3;                    // NOTEXIST
        if (skillData == null || !skillData.ClassName.Contains("HuxleySkillData_",
                StringComparison.Ordinal)) return 10;                    // NOT_SKILL_ITEM
        if (_serverEvents.BlocksPcRoomItem(skillData)) return 48;         // PCROOM_ITEM
        // POS_Helmet is EItemPos 0, so the cooked helmet skills have no ItemPos at all.
        string skillPart = skillData.ArmorPart.Length != 0 ? skillData.ArmorPart : "POS_Helmet";
        if (armorData == null || armorData.ArmorPart.Length == 0
            || !armorData.ArmorPart.Equals(skillPart, StringComparison.OrdinalIgnoreCase))
            return 19;                                                    // SKILL_WRONG_POS
        int expectedCell = skillPart.ToLowerInvariant() switch
        {
            "pos_helmet" => 0, "pos_upbody" => 1, "pos_lowbody" => 2,
            "pos_gloves" => 3, "pos_boots" => 4, _ => -1
        };
        if (expectedCell != armorCell) return 19;
        if (socket >= armorData.SkillSocketShapes.Count) return 17;       // SKILL_SLOT_NOT_FOUND
        if (!armorData.SkillSocketShapes[socket].Equals(skillData.SkillItemShape,
                StringComparison.Ordinal)) return 19;                    // SKILL_WRONG_POS
        if ((socket == 0 ? armor.Skill : armor.Skill1) != 0) return 18;   // SKILL_SLOT_INVALID
        if (_playerStore.IsSocketedSkillCell(pid, skillCell)) return 18;  // cell holds an equipped armor's skill
        var progress = _playerStore.GetProgress(pid);
        if (progress.Level < skillData.EquipLevel) return 40;
        if (progress.Rank < skillData.EquipRank) return 41;
        string playerStyle = _characterStore.Get(pid)?.Style switch
        {
            1 => "CombatStyle_Enforcer", 2 => "CombatStyle_Avenger",
            3 => "CombatStyle_Phantom", _ => ""
        };
        if (skillData.CombatStyle.Length != 0 && skillData.CombatStyle != "CombatStyle_None"
            && skillData.CombatStyle != playerStyle) return 15;
        return 0;
    }

    public byte ValidateVehicle(int pid, byte mainCell, PlayerStore.GenericItem? source,
        ItemCatalog.Item? data)
    {
        if (mainCell >= _playerStore.MainCapacity(pid)) return 1;         // Mismatch
        if (source == null) return 6;                                    // NotFoundVehicle
        if (data?.Vehicle != true) return 5;                             // NotVehicle
        var progress = _playerStore.GetProgress(pid);
        if (progress.Level < data.EquipLevel || progress.Rank < data.EquipRank) return 1;
        return 0;
    }

    public byte ValidateUseItem(int pid, byte mainCell, PlayerStore.GenericItem? source,
        ItemCatalog.Item? data)
    {
        if (mainCell >= _playerStore.MainCapacity(pid)) return 6;         // INVEN_OUTOFBOUND
        if (source == null) return 3;                                    // NOTEXIST
        if (data == null) return 7;                                      // INVALID_ITEMID
        var progress = _playerStore.GetProgress(pid);
        if (progress.Level < data.EquipLevel) return 40;
        if (progress.Rank < data.EquipRank) return 41;
        return 0;
    }
}

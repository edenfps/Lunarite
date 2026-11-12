using Lunarite.Packets.Common;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;
using Lunarite.World;

using Microsoft.Extensions.DependencyInjection;

internal sealed partial class TestSuite
{
    // docs/WARDROBE.md: collection per account and species, style filter, looks shown to other players.
    private void WardrobeLooks()
    {
        var wardrobe = _services.GetRequiredService<WardrobeManager>();
        var store = _services.GetRequiredService<WardrobeStore>();

        int FindArmor(string part, string style, int skip = 0) => Enumerable.Range(200000, 100000)
            .Where(id => _itemCatalog.Get(id) is { } d && d.ArmorPart.Equals(part, StringComparison.OrdinalIgnoreCase)
                && d.CombatStyle == style)
            .Skip(skip).FirstOrDefault();
        int enforcerHelmet = FindArmor("POS_Helmet", "CombatStyle_Enforcer");
        int enforcerHelmet2 = FindArmor("POS_Helmet", "CombatStyle_Enforcer", 1);
        int avengerHelmet = FindArmor("POS_Helmet", "CombatStyle_Avenger");
        int enforcerBoots = FindArmor("POS_Boots", "CombatStyle_Enforcer");
        Check(enforcerHelmet != 0 && enforcerHelmet2 != 0 && avengerHelmet != 0 && enforcerBoots != 0,
            "wardrobe test found Enforcer and Avenger armor in the item table");
        Check(wardrobe.PartOf(enforcerHelmet) == 0 && wardrobe.PartOf(enforcerBoots) == 4 && wardrobe.PartOf(100100) == -1,
            "wardrobe maps armor to ARMOR cells and ignores weapons");

        const int owner = 990077;
        var holder = _characterStore.Create(owner, "WardrobeA", 1, new Custom());
        var sameSpecies = _characterStore.Create(owner, "WardrobeB", 1, new Custom());
        var otherSpecies = _characterStore.Create(owner, "WardrobeC", 6, new Custom());
        _characterStore.SetStyle(holder.Pid, 1);
        _characterStore.SetStyle(sameSpecies.Pid, 1);
        _characterStore.SetStyle(otherSpecies.Pid, 1);
        var held = _playerStore.GetInventory(holder.Pid, () => new());
        byte cell = 0;
        foreach (int id in new[] { enforcerHelmet, avengerHelmet, enforcerBoots, 100100 })
            held.Add(new PlayerStore.GenericItem { InventoryIndex = InventoryType.Main, InventoryCell = cell++, ItemId = id });

        Check(wardrobe.CollectFrom(holder.Pid) == 3, "wardrobe collects every armor piece held, not weapons");
        Check(wardrobe.CollectFrom(holder.Pid) == 0, "wardrobe collecting again adds nothing");
        Check(wardrobe.Available(sameSpecies.Pid, 0).SequenceEqual([enforcerHelmet]),
            "wardrobe: another character of the same species and account sees the piece, filtered to its style");
        Check(wardrobe.Available(otherSpecies.Pid, 0).Count == 0,
            "wardrobe: a character of another species (race/gender) on the same account sees nothing");

        // Looks only cover an equipped piece; other players see the look's ID, or nothing for a hidden helmet.
        Check(wardrobe.Shown(sameSpecies.Pid, 0, enforcerHelmet2) == enforcerHelmet2, "no look: real armor shown");
        store.SetLook(sameSpecies.Pid, 0, enforcerHelmet);
        Check(wardrobe.Shown(sameSpecies.Pid, 0, enforcerHelmet2) == enforcerHelmet, "look replaces the shown helmet");
        Check(wardrobe.Shown(sameSpecies.Pid, 0, 0) == 0, "look over an empty slot shows nothing");
        var shownItem = wardrobe.ShownItem(sameSpecies.Pid, new PlayerStore.GenericItem
            { InventoryIndex = InventoryType.Armor, InventoryCell = 0, ItemId = enforcerHelmet2, Durability = 37 });
        Check(shownItem?.ItemId == enforcerHelmet && shownItem.Durability == 37,
            "shown item keeps the real piece's state with the look's ID");
        store.SetLook(sameSpecies.Pid, 0, WardrobeStore.HiddenHelmet);
        Check(wardrobe.Shown(sameSpecies.Pid, 0, enforcerHelmet2) == 0
            && wardrobe.ArmorOther(sameSpecies.Pid, 0, new PlayerStore.GenericItem
                { InventoryIndex = InventoryType.Armor, InventoryCell = 0, ItemId = enforcerHelmet2 }) is EquipArmorOther { ItemId: 0 },
            "hidden helmet: other players get an empty head");
        store.SetLook(sameSpecies.Pid, 4, avengerHelmet);
        Check(wardrobe.Shown(sameSpecies.Pid, 4, enforcerBoots) == enforcerBoots,
            "a stored look of the wrong part or style is ignored");
        store.SetLook(sameSpecies.Pid, 0, WardrobeStore.OwnArmor);
        store.SetLook(sameSpecies.Pid, 4, WardrobeStore.OwnArmor);
        Check(store.Looks(sameSpecies.Pid).All(x => x == 0), "reset restores own armor");

        Check(WardrobeManager.IsCommand("/wardrobe list") && WardrobeManager.IsCommand("/WARDROBE")
            && !WardrobeManager.IsCommand("/wardrobes") && !WardrobeManager.IsCommand("hello /wardrobe"),
            "wardrobe chat commands are recognised exactly");
    }
}

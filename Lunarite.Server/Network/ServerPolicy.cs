namespace Lunarite.Network;

// Private-server choices that deliberately differ from retail. Kept free of store state so
// setting a flag never loads persistence as a side effect.
public static class ServerPolicy
{
    // Weapon slots 3 and 4 open for every character without the OpenWeaponSlot cash item.
    public static bool OpenAllWeaponSlots = true;

    // Every character has the fully expanded inventory (78) and bank (40) the cash-shop modules
    // would buy. Steps only rise, so turning this off keeps what was granted.
    public static bool MaxExpansionByDefault = true;
}

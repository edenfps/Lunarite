using Lunarite.Handlers;
using Lunarite.Packets.Outgoing;

namespace Lunarite.World;

public static class VehicleManager
{

    public static UpdateVehicle BuildExpiredVehiclePresence(int pid) =>
        new() { Pid = pid, VehicleItemId = 0 };

    public static bool ClearMountedVehicleOnPresenceExit(int pid, out int vehicleItemId) =>
        Game.PlayerFor(pid).TryClearSummonedVehicle(out vehicleItemId);
}

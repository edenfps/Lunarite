using System.Collections.Generic;
using System.Threading;

namespace Lunarite.World;

// A character in the world: where it is and what it is doing while selected. Persistent data
// (inventory, progress, quests) stays in the stores; this is the runtime state around it.
public sealed class Player
{
    private readonly object _lock = new();
    private int _spawned;
    private byte? _transportRoute;

    public Player(int pid)
    {
        Pid = pid;
    }

    public int Pid { get; }

    // City zone the character is in; null until it enters one or after it leaves the world.
    public int? CityMap { get; set; }

    // Last spawn (op17) or position report (op25).
    public (float X, float Y, float Z, short Yaw)? Position { get; set; }

    // Zone an op20 map move came from; op139 MoveMapReturn goes back there.
    public (int Map, int Area)? MoveMapReturn { get; set; }

    // Vehicle item the character rides (op158).
    public int? SummonedVehicle { get; set; }

    public bool TryClearSummonedVehicle(out int vehicleItemId)
    {
        lock (_lock)
        {
            vehicleItemId = SummonedVehicle ?? 0;
            bool summoned = SummonedVehicle.HasValue;
            SummonedVehicle = null;
            return summoned;
        }
    }

    // Environment.TickCount64 of the last /unstuck.
    public long? UnstuckAt { get; set; }

    // Vendor sales that op177 can still undo; null until the first sale of this selection.
    public List<UndoItem>? UndoSales { get; set; }

    // Spawned characters are visible to others in the same zone.
    public bool IsSpawned => Volatile.Read(ref _spawned) != 0;

    public void MarkSpawned() => Volatile.Write(ref _spawned, 1);

    public bool TryMarkSpawned() => Interlocked.Exchange(ref _spawned, 1) == 0;

    public bool TryClearSpawned() => Interlocked.Exchange(ref _spawned, 0) != 0;

    // Tram route the character is riding (op104/op105).
    public bool IsOnTransport
    {
        get { lock (_lock) return _transportRoute.HasValue; }
    }

    public bool TryBoardTransport(byte route)
    {
        lock (_lock)
        {
            if (_transportRoute.HasValue) return false;
            _transportRoute = route;
            return true;
        }
    }

    public bool TryLeaveTransport()
    {
        lock (_lock)
        {
            bool riding = _transportRoute.HasValue;
            _transportRoute = null;
            return riding;
        }
    }

    public (int Map, int Area)? TakeMoveMapReturn()
    {
        lock (_lock)
        {
            var previous = MoveMapReturn;
            MoveMapReturn = null;
            return previous;
        }
    }

    public List<UndoItem> UndoSalesOrCreate()
    {
        lock (_lock) return UndoSales ??= [];
    }
}

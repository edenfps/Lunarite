using System;
using System.IO;
using System.Text.Json;

using Lunarite.Resources;
using Lunarite.Storage;

namespace Lunarite.World;

// Operator-controlled server events, persisted with the other stores (events.json row).
// PC-room event: every character is treated as a retail PC-room session. The client then applies
// its own PCROOM_BONUS (+50% BP and PCr on mission/battle results) and allows EISC_PCRoom items
// (equip, buy, tune-up); the server lifts the matching EISC_PCRoom refusals. Transfers (mail,
// trade) of PC-room items stay blocked as in retail.
public sealed class ServerEvents
{
    private readonly AtomicJson _atomicJson;


    public sealed class State { public bool PcRoom { get; set; } }

    private static readonly object Lock = new();
    private static readonly string PathName = Path.Combine(AppContext.BaseDirectory, "events.json");
    private State? _state;

    public ServerEvents(AtomicJson atomicJson)
    {
        _atomicJson = atomicJson;
    }

    private State Current() => _state ??= _atomicJson.Read<State>(PathName) ?? new State();

    public bool PcRoomActive { get { lock (Lock) return Current().PcRoom; } }

    public void SetPcRoom(bool active)
    {
        lock (Lock)
        {
            Current().PcRoom = active;
            _atomicJson.Write(PathName, JsonSerializer.Serialize(_state));
        }
    }

    // EISC_PCRoom (ItemSalesCategory 1) refusals apply only outside a PC-room event.
    public bool BlocksPcRoomItem(ItemCatalog.Item? data) =>
        data?.ItemSalesCategory == 1 && !PcRoomActive;

    internal void ResetForTest()
    {
        lock (Lock) { _state = null; _atomicJson.Delete(PathName); }
    }
}

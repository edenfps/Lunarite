using System;

using Lunarite.Storage;

namespace Lunarite.World;

// Retail server op27 is the authority for all eight faction economy multipliers. The native
// client only parses and forwards these floats to HuxleySubsystem.OnPriceRate; it does not derive
// them from BasePriceRate/BuyPriceRate/SellPriceRate. Keep one snapshot for both the packet and
// server-side transactions so displayed and charged prices cannot diverge.
public sealed class EconomyRates
{
    private readonly CharacterStore _characterStore;

    public readonly record struct Snapshot(
        float SapiBuy, float SapiSell, float AlterBuy, float AlterSell,
        float SapiRepair, float AlterRepair, float SapiTuneUp, float AlterTuneUp)
    {
        public static Snapshot Neutral => new(1, 1, 1, 1, 1, 1, 1, 1);
    }

    public readonly record struct Faction(float Buy, float Sell, float Repair, float TuneUp);

    private static readonly object Lock = new();
    private Snapshot _current = Snapshot.Neutral;

    public EconomyRates(CharacterStore characterStore)
    {
        _characterStore = characterStore;
    }

    public Snapshot Current { get { lock (Lock) return _current; } }

    public bool TrySetAuthoritative(Snapshot rates)
    {
        float[] values =
        [
            rates.SapiBuy, rates.SapiSell, rates.AlterBuy, rates.AlterSell,
            rates.SapiRepair, rates.AlterRepair, rates.SapiTuneUp, rates.AlterTuneUp
        ];
        if (Array.Exists(values, value => !float.IsFinite(value) || value <= 0)) return false;
        lock (Lock) _current = rates;
        return true;
    }

    public Faction ForCharacterIndex(byte index)
    {
        Snapshot rates = Current;
        return index >= 10
            ? new(rates.AlterBuy, rates.AlterSell, rates.AlterRepair, rates.AlterTuneUp)
            : new(rates.SapiBuy, rates.SapiSell, rates.SapiRepair, rates.SapiTuneUp);
    }

    // Synthetic/test PIDs without a retail character record retain neutral economics.
    public Faction ForPid(int pid)
    {
        var character = _characterStore.Get(pid);
        return character == null ? new(1, 1, 1, 1) : ForCharacterIndex(character.Index);
    }

    public void ResetNeutral() { lock (Lock) _current = Snapshot.Neutral; }
}

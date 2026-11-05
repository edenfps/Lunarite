using System;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;

namespace Lunarite.Handlers;

[PacketHandler]
public static class GetLicenseHandler
{
    private static MultiStoreTransaction _multiStoreTransaction = null!;
    private static LicenseStore _licenseStore = null!;
    private static PlayerStore _playerStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _licenseStore = serviceProvider.GetRequiredService<LicenseStore>();
        _playerStore = serviceProvider.GetRequiredService<PlayerStore>();
        _multiStoreTransaction = serviceProvider.GetRequiredService<MultiStoreTransaction>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!reader.TryRead(out byte plus) || !reader.TryRead(out int licenseId)
            || reader.RemainingLength != 0) return false;
        int pid = Game.PidOf(client);
        LicenseStore.AcquireResult result = LicenseStore.AcquireResult.Error;
        if (plus == 1)
            _multiStoreTransaction.Run(() => result = _licenseStore.TryAcquire(pid, licenseId));
        client.SendPacket(new ProcLicense { Plus = plus != 0, Result = (byte)result, License = licenseId });
        if (result == LicenseStore.AcquireResult.Ok)
        {
            long lp = _playerStore.GetProgress(pid).LP;
            client.SendPacket(new Parameter { Param = Parameter.PARAMETER_LP, Value = (int)Math.Min(int.MaxValue, lp) });
        }
        return true;
    }
}

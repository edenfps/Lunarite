using System;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;

namespace Lunarite.Handlers;

[PacketHandler]
public static class NewMemberHandler
{
    private static AccountStore _accountStore = null!;
    private static AccountSessionStore _accountSessionStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _accountStore = serviceProvider.GetRequiredService<AccountStore>();
        _accountSessionStore = serviceProvider.GetRequiredService<AccountSessionStore>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        // SendNewMember is native op6 `ss`; this development server accepts the
        // same local credentials that its passwordless login endpoint accepts.
        if (!reader.TryRead(out string? id) || !reader.TryRead(out string? password)
            || reader.RemainingLength != 0) return false;
        // Every registration that hashes a password counts against the address's hourly limit;
        // past it NEWMEMBER_ERROR, without hashing. The slot is reserved first so parallel
        // sockets cannot overshoot it.
        var result = AccountStore.RegistrationResult.Error;
        if (_accountSessionStore.TryBeginRegistration(client.IPAddress, out var reservation))
        {
            bool hashed = false;
            try { result = _accountStore.Register(id ?? "", password ?? "", out _, out hashed); }
            finally { if (!hashed) _accountSessionStore.CancelRegistration(client.IPAddress, reservation); }
        }
        byte answer = (byte)result;
        client.SendPacket(new NewMember { Answer = answer });
        return true;
    }
}

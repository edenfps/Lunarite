using System;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Outgoing;
using Lunarite.Resources;
using Lunarite.Storage;

namespace Lunarite.Handlers;

[PacketHandler]
public static class GaugeBarGameResultHandler
{
    private static PersonalBuffCatalog _personalBuffCatalog = null!;
    private static PersonalBuffStore _personalBuffStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _personalBuffStore = serviceProvider.GetRequiredService<PersonalBuffStore>();
        _personalBuffCatalog = serviceProvider.GetRequiredService<PersonalBuffCatalog>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        int pid = Game.PidOf(client);
        if (!reader.TryRead(out int setterId) || !reader.TryRead(out int playSeconds)
            || reader.RemainingLength != 0 || pid <= 0)
            return false;
        var setter = _personalBuffCatalog.GetSetter(setterId);
        if (setter == null)
        {
            client.SendPacket(new PersonalBuffResult { Action = BuffResultCode.UndefinedSetter });
            return true;
        }
        if (playSeconds < 0 || !client.Session.TryTakePendingDrink(out int purchasedSetter)
            || purchasedSetter != setterId)
        {
            client.SendPacket(new PersonalBuffResult { Action = BuffResultCode.CantUse });
            return true;
        }

        int phase = (int)((long)playSeconds * setter.GaugeSpeed % 200);
        int position = phase <= 100 ? phase : 200 - phase;
        int centerDistance = Math.Abs(position - 50);
        PersonalBuffCatalog.Choice choice;
        // SIDE_EFFECT_BASE_RATIO says 100 is one percent, hence a 10,000-point roll.
        if (Random.Shared.Next(10_000) < setter.SideEffectProbability)
            choice = setter.SideEffect;
        else if (centerDistance * 2 <= setter.Great.GoalRatio)
            choice = setter.Great;
        else if (centerDistance * 2 <= setter.Great.GoalRatio + setter.Success.GoalRatio)
            choice = setter.Success;
        else
            choice = setter.Handicap;

        var definition = _personalBuffCatalog.GetBuff(choice.BuffId);
        if (definition == null)
        {
            client.SendPacket(new PersonalBuffResult { Action = BuffResultCode.UndefinedBuff });
            return true;
        }
        var buff = new PersonalBuff
        {
            ID = definition.Id, Group = definition.Group, ModifierType = definition.ModifierType,
            CanBeCumulated = definition.CanBeCumulated, Level = 1,
            ExpirationDate = checked((int)DateTimeOffset.UtcNow
                .AddSeconds(definition.DurationSeconds).ToUnixTimeSeconds())
        };
        byte action = _personalBuffStore.Apply(pid, buff, choice.MaxLevel, out var applied);
        client.SendPacket(new PersonalBuffResult
        {
            Action = action,
            Buff = action is >= BuffResultCode.Added and <= BuffResultCode.DecrementLevel ? applied : null
        });
        return true;
    }
}

using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.Storage;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class PositionHandler
{
    private static CityManager _cityManager = null!;
    private static ILogger _logger = null!;
    private static CharacterStore _characterStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(PositionHandler));

        _characterStore = serviceProvider.GetRequiredService<CharacterStore>();
        _cityManager = serviceProvider.GetRequiredService<CityManager>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        // SendPosition is op25 `b`. HuxleyMMOPlayerController emits it whenever the pawn enters
        // a primary HuxleyCityAreaVolume; it becomes the resume area for this zone.
        if (!reader.TryRead(out byte area) || reader.RemainingLength != 0) return false;
        int pid = Game.PidOf(client);
        if (pid > 0)
        {
            int map = (Game.PlayerFor(pid).CityMap ?? _characterStore.GetCityLocation(pid).Map);
            byte characterIndex = _characterStore.Get(pid)?.Index ?? 0;
            if (_cityManager.IsReportedCityAreaValid(map, area, characterIndex))
                _characterStore.SetCityLocation(pid, map, area);
            else
                _logger.LogWarning("[City] rejected position area {0} for PID {1}, map {2}.",
                    area, pid, map);
        }
        return true;
    }
}

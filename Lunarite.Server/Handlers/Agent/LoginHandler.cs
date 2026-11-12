using System;
using System.Linq;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.Packets.Incoming;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class LoginHandler
{
    // Serialises the duplicate-login check and the account binding.
    private static readonly object LoginLock = new();

    public const string RememberLogin = "#REMEMBER";
    public const string ForgetLogin = "#FORGET";

    private static CharacterManager _characterManager = null!;

    private static QuestInstanceManager _questInstanceManager = null!;
    private static ILogger _logger = null!;
    private static AccountStore _accountStore = null!;
    private static AccountSessionStore _accountSessionStore = null!;
    private static PlayerManager _playerManager = null!;
    private static BattleFront _battleFront = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _questInstanceManager = serviceProvider.GetRequiredService<QuestInstanceManager>();
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(LoginHandler));

        _accountStore = serviceProvider.GetRequiredService<AccountStore>();
        _accountSessionStore = serviceProvider.GetRequiredService<AccountSessionStore>();
        _characterManager = serviceProvider.GetRequiredService<CharacterManager>();
        _playerManager = serviceProvider.GetRequiredService<PlayerManager>();
        _battleFront = serviceProvider.GetRequiredService<BattleFront>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!LoginRequest.TryDeserialize(ref reader, out var packet))
        {
            _logger.LogWarning("Failed to deserialize {0}.", nameof(LoginRequest));
            return false;
        }

        _logger.LogDebug("Received {0} packet. ( {1} )", nameof(LoginRequest), packet);

        // "Remember me" (LunariteLoginGame): right after its real login the client sends a second op7 with this
        // name and a token it made, which the logged-in account then accepts in place of its password. Turning
        // it off sends #FORGET with the token. Neither is answered; "#" is never in a login name.
        if (packet.Id == RememberLogin)
        {
            if (client.Session.AccountId is int rememberUser
                && _accountStore.Remember(rememberUser, packet.Pwd ?? ""))
                _logger.LogInformation("[Accounts] user {0} remembered a login.", rememberUser);
            return true;
        }
        if (packet.Id == ForgetLogin)
        {
            if (_accountStore.Forget(packet.Pwd ?? ""))
                _logger.LogInformation("[Accounts] a remembered login was forgotten.");
            return true;
        }

        // Too many wrong logins for this name or from this address: LOGIN_ERROR without hashing.
        int userId = 0;
        AccountStore.LoginResult result = AccountStore.LoginResult.Error;
        if (_accountSessionStore.TryBeginLogin(client.IPAddress, packet.Id ?? "", out var attempt))
        {
            try { result = _accountStore.Authenticate(packet.Id ?? "", packet.Pwd ?? "", out userId); }
            finally
            {
                _accountSessionStore.EndLogin(attempt!,
                    result is AccountStore.LoginResult.WrongPassword or AccountStore.LoginResult.NoneMember);
            }
        }
        lock (LoginLock)
        {
            if (result == AccountStore.LoginResult.Ok)
            {
                bool agentDuplicate = client.Server.Clients.Any(other => other != client && other.Session.AccountId == userId);
                if (!agentDuplicate && _characterManager.HasActiveAccount(userId)) _questInstanceManager.ReleaseStaleQuestSessions(userId);
                if (agentDuplicate || _characterManager.HasActiveAccount(userId))
                    result = AccountStore.LoginResult.SameUser;
            }
            if (result == AccountStore.LoginResult.Ok) client.Session.AccountId = userId;
        }
        bool accepted = result == AccountStore.LoginResult.Ok;
        var loginAnswer = new Login
        {
            Answer = (byte)result,
            UserId = userId,
            Teenager = true
        };

        client.SendPacket(loginAnswer);

        if (!accepted)
        {
            _logger.LogWarning("[Accounts] rejected login '{0}'.", packet.Id ?? "");
            return true;
        }
        // The launcher on a GM's address gets the console-enabled files from now on (PatchServer).
        if (World.GmCommands.IsGmLogin(packet.Id ?? "", Environment.GetEnvironmentVariable("HUXLEY_GM_LOGINS")))
        {
            client.Session.IsGm = true;
            PatchServer.ReportGm(client.IPAddress);
        }
        var serverList = new ServerList
        {
            Servers =
            {
                new ServerList.GameServer
                {
                    Server = 1,
                    IP = Agent.GameAddressFor(client),
                    Name = Agent.ServerName,
                    Percet = Agent.LoadPercent(_playerManager.OnlinePids.Count, Agent.ServerCapacity),
                    Camp = _battleFront.DominantSpecies()
                }
            }
        };

        client.SendPacket(serverList);

        return true;
    }
}

using System.Buffers.Binary;
using System.Net;

using Lunarite.Packets.Common;
using Lunarite.Packets.Incoming;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;
using Lunarite.World;

internal sealed partial class TestSuite
{
    private void AccountLoginAndRegistration()
    {
        Check(_accountStore.TryLogin("owner-a", "secret-a", out _accountA) && _accountA == 69,
            "first private-server login provisions legacy account 69");
        Check(_accountStore.Authenticate("owner-a", "wrong", out _) == AccountStore.LoginResult.WrongPassword
            && _accountStore.Authenticate("unknownaccount", "secret", out _) == AccountStore.LoginResult.NoneMember
            && _accountStore.Authenticate("unknown-account", "secret", out _) == AccountStore.LoginResult.WrongId
            && _accountStore.Register("bad name", "secret", out _) == AccountStore.RegistrationResult.WrongId
            && _accountStore.Authenticate("owner-a", "", out _) == AccountStore.LoginResult.WrongPassword,
            "account login preserves native unknown-ID and wrong-password results");
        Check((byte)AccountStore.LoginResult.SameUser == 3
            && new Login { Answer = (byte)AccountStore.LoginResult.SameUser }.Serialize()
                .SequenceEqual(new byte[] { 7, 3 }),
            "duplicate account sessions use native LOGIN_SAMEUSER without a success payload");
        Check(_accountStore.TryRegister("ownerb", "secret-b", out _accountB) && _accountB != _accountA,
            "registration assigns a distinct persistent account id");
        Check(_accountStore.Register("ownerb", "different", out _) == AccountStore.RegistrationResult.Duplicate
            && _accountStore.Register("   ", "secret", out _) == AccountStore.RegistrationResult.WrongId
            && _accountStore.Register("newowner", "", out _) == AccountStore.RegistrationResult.Error,
            "registration preserves native duplicate, invalid-ID, and generic failure results");
        Check(_accountStore.TryAdjustCash(_accountA, -_accountStore.GetCash(_accountA), out int zeroCash) && zeroCash == 0
            && _accountStore.TryAdjustCash(_accountA, 2500, out int creditedCash) && creditedCash == 2500
            && !_accountStore.TryAdjustCash(_accountA, -2501, out _)
            && new PayItem { Subtype = 0, Result = 7, Extra = BitConverter.GetBytes(creditedCash) }.Serialize()
                .SequenceEqual(new byte[] { 227, 0, 7, 0xC4, 0x09, 0, 0 })
            && _accountStore.TryAdjustCash(_accountA, -2500, out zeroCash) && zeroCash == 0,
            "cash balance is account-scoped, persistent, bounded, and serialized by exact op227 subtype 0");
        _accountSessionStore.Issue(System.Net.IPAddress.Parse("192.0.2.9"), _accountB);
        Check(_accountSessionStore.TryClaim(System.Net.IPAddress.Parse("::ffff:192.0.2.9"), out int claimed)
            && claimed == _accountB && !_accountSessionStore.TryClaim(System.Net.IPAddress.Parse("192.0.2.9"), out _),
            "Agent-to-Game ticket is one-time and address-bound");
        var sharedNat = System.Net.IPAddress.Parse("192.0.2.10");
        _accountSessionStore.Issue(sharedNat, _accountA);
        _accountSessionStore.Issue(sharedNat, _accountB);
        Check(!_accountSessionStore.TryClaim(sharedNat, out _)
            && !_accountSessionStore.TryClaim(sharedNat, out _),
            "different accounts behind one NAT fail closed instead of claiming each other's Game handoff");
        var trustedNat = IPAddress.Parse("192.0.2.14");
        var trustedLocalA = IPAddress.Parse("10.1.0.2");
        var trustedLocalB = IPAddress.Parse("10.1.0.3");
        _accountSessionStore.Issue(trustedNat, _accountA, trustedLocalA, trustClientLocalAddress: true);
        _accountSessionStore.Issue(trustedNat, _accountB, trustedLocalB, trustClientLocalAddress: true);
        Check(_accountSessionStore.TryClaim(trustedNat, out int trustedClaimB, trustedLocalB,
                trustClientLocalAddress: true) && trustedClaimB == _accountB
            && _accountSessionStore.TryClaim(trustedNat, out int trustedClaimA, trustedLocalA,
                trustClientLocalAddress: true) && trustedClaimA == _accountA
            && !_accountSessionStore.TryClaim(trustedNat, out _, trustedLocalA,
                trustClientLocalAddress: true),
            "opt-in trusted-NAT handoff separates distinct client-local IPv4 values without FIFO guessing");
        _accountSessionStore.Issue(trustedNat, _accountA, trustedLocalA, trustClientLocalAddress: true);
        _accountSessionStore.Issue(trustedNat, _accountB, trustedLocalA, trustClientLocalAddress: true);
        Check(!_accountSessionStore.TryClaim(trustedNat, out _, trustedLocalA,
                trustClientLocalAddress: true),
            "opt-in trusted-NAT handoff still fails closed when two accounts report the same local IPv4");
        var repeatedLogin = System.Net.IPAddress.Parse("192.0.2.11");
        _accountSessionStore.Issue(repeatedLogin, _accountA);
        _accountSessionStore.Issue(repeatedLogin, _accountA);
        Check(_accountSessionStore.TryClaim(repeatedLogin, out claimed) && claimed == _accountA
            && !_accountSessionStore.TryClaim(repeatedLogin, out _),
            "a repeated SelectServer replaces the account's handoff instead of adding a second Game link");
        Check(CharacterManager.IsGameVerifyAuthorized(true, _accountA, _accountA)
            && !CharacterManager.IsGameVerifyAuthorized(true, _accountB, _accountA)
            && !CharacterManager.IsGameVerifyAuthorized(false, _accountA, _accountA)
            && !CharacterManager.IsGameVerifyAuthorized(true, _accountA, 0),
            "Game Verify accepts only the UID bound by the authenticated Agent handoff");
        _ownedA = _characterStore.Create(_accountA, "AccountA", 1, new Custom());
        _ownedB = _characterStore.Create(_accountB, "AccountB", 6, new Custom());
        Check(_characterStore.All(_accountA).Select(x => x.Pid).SequenceEqual(new[] { _ownedA.Pid })
            && _characterStore.GetOwned(_accountA, _ownedB.Pid) is null
            && _characterStore.GetOwned(_accountB, _ownedB.Pid) is not null,
            "character rosters and PID access are isolated by owning account");
        Check(_characterStore.TrySetInitialStyle(_ownedA.Pid, 1)
            && !_characterStore.TrySetInitialStyle(_ownedA.Pid, 2)
            && _characterStore.Get(_ownedA.Pid)?.Style == 1,
            "combat style is a one-time persisted character choice");
        var playerInfoLayout = new PlayerInfo
        {
            Pid = _ownedA.Pid,
            Name = "P",
            CharInfo = new CharInfo
            {
                Level = 0x1234, Style = 1, Rank = 2, Job = 3,
                LunaritesFuel = 0x11223344, Nick = 0x22334455, Title = 0x33445566
            },
            PointInfo = new PointInfo { BP = 0x10203040, LP = 0x20304050 },
            Medals = [new MedalInfo(), new MedalInfo()],
            Items =
            [
                new PlayerInfo.Item
                {
                    ItemID = 200499, Count = 1, InventoryIndex = 4, Cell = 4, State = 2,
                    LimitTime = 7, TuneUpOptions = 8, Skill0 = 303400, Skill1 = 9, Durability = 100
                }
            ]
        }.Serialize();
        const int playerInfoCharOffset = 42; // op + PID + "P\0" + bbdd + 25-byte appearance
        Check(playerInfoLayout.AsSpan(playerInfoCharOffset, 20).SequenceEqual(new byte[]
            {
                0x34, 0x12, 1, 2, 3, 0, 0, 0,
                0x44, 0x33, 0x22, 0x11, 0x55, 0x44, 0x33, 0x22, 0x66, 0x55, 0x44, 0x33
            })
            && BinaryPrimitives.ReadInt32LittleEndian(playerInfoLayout.AsSpan(playerInfoCharOffset + 20)) == 0x10203040
            && BinaryPrimitives.ReadInt32LittleEndian(playerInfoLayout.AsSpan(playerInfoCharOffset + 24)) == 0x20304050,
            "PlayerInfo op11 uses native 20-byte CharInfo offsets without shifting style or points");
        const int playerInfoInventoryOffset = 111;
        Check(playerInfoLayout.AsSpan(playerInfoInventoryOffset, 7).SequenceEqual(new byte[] { 0, 0, 0, 0, 1, 0, 0 })
            && playerInfoLayout.Length == playerInfoInventoryOffset + 7 + 32
            && BinaryPrimitives.ReadInt32LittleEndian(playerInfoLayout.AsSpan(playerInfoInventoryOffset + 7)) == 200499
            && playerInfoLayout[playerInfoInventoryOffset + 15] == 4
            && playerInfoLayout[playerInfoInventoryOffset + 16] == 4
            && BinaryPrimitives.ReadInt32LittleEndian(playerInfoLayout.AsSpan(playerInfoInventoryOffset + 27)) == 303400
            && BinaryPrimitives.ReadInt32LittleEndian(playerInfoLayout.AsSpan(playerInfoInventoryOffset + 35)) == 100,
            "PlayerInfo op11 carries native inventory counts and 32-byte equipped-item records");
        var boundedPlayerInfo = new PlayerInfo
        {
            Name = "P",
            Medals = [new MedalInfo(), new MedalInfo()],
            PersonalBuffs = Enumerable.Repeat(new PersonalBuff(), 256).ToList(),
            LicenseList = Enumerable.Range(1, 256).ToList()
        }.Serialize();
        const int boundedBuffCountOffset = 109;
        const int boundedLicenseCountOffset = boundedBuffCountOffset + 1 + byte.MaxValue * 12;
        const int boundedInventoryOffset = boundedLicenseCountOffset + 1 + byte.MaxValue * 4;
        Check(boundedPlayerInfo[boundedBuffCountOffset] == byte.MaxValue
            && boundedPlayerInfo[boundedLicenseCountOffset] == byte.MaxValue
            && boundedPlayerInfo.Length == boundedInventoryOffset + 7,
            "PlayerInfo op11 caps byte-counted buff and license arrays without corrupting the inventory tail");
    }

    private void AccountBansAndListenEndpoint()
    {
        string banLogin = "banprobe" + Environment.ProcessId;
        _accountStore.Register(banLogin, "pw-123", out _);
        int bannedUser = _accountStore.SetBanned(banLogin, true);
        var bannedLogin = _accountStore.Authenticate(banLogin, "pw-123", out _);
        var bannedWrongPassword = _accountStore.Authenticate(banLogin, "nope", out _);
        _accountStore.SetBanned(banLogin, false);
        Check(bannedUser > 0 && bannedLogin == AccountStore.LoginResult.Disabled
            && bannedWrongPassword == AccountStore.LoginResult.WrongPassword
            && _accountStore.Authenticate(banLogin, "pw-123", out _) == AccountStore.LoginResult.Ok
            && _accountStore.SetBanned("no-such-login-" + Environment.ProcessId, true) == 0,
            "GM bans return native LOGIN_DISABLED only after the password checks, and unban restores login");
        string rememberLogin = "remember" + Environment.ProcessId;
        _accountStore.Register(rememberLogin, "pw-remember", out int rememberUser);
        const string token = "Ab3dE5gH9jK1";
        bool unknownToken = _accountStore.Authenticate(rememberLogin, token, out _) == AccountStore.LoginResult.WrongPassword;
        bool stored = _accountStore.Remember(rememberUser, token) && !_accountStore.Remember(rememberUser, "short")
            && !_accountStore.Remember(rememberUser, "has-dash-123");
        bool tokenLogin = _accountStore.Authenticate(rememberLogin, token, out int tokenUser) == AccountStore.LoginResult.Ok
            && tokenUser == rememberUser;
        bool passwordStillWorks = _accountStore.Authenticate(rememberLogin, "pw-remember", out _) == AccountStore.LoginResult.Ok;
        _accountStore.SetBanned(rememberLogin, true);
        bool bannedToken = _accountStore.Authenticate(rememberLogin, token, out _) == AccountStore.LoginResult.Disabled;
        _accountStore.SetBanned(rememberLogin, false);
        bool forgotten = _accountStore.Forget(token)
            && _accountStore.Authenticate(rememberLogin, token, out _) == AccountStore.LoginResult.WrongPassword;
        for (int i = 0; i < AccountStore.MaxRememberedLogins + 1; i++) _accountStore.Remember(rememberUser, "Token00000" + i + "x");
        bool oldestDropped = _accountStore.Authenticate(rememberLogin, "Token000000x", out _) == AccountStore.LoginResult.WrongPassword
            && _accountStore.Authenticate(rememberLogin, "Token000005x", out _) == AccountStore.LoginResult.Ok;
        bool resetClears = _accountStore.TryResetPassword(rememberLogin, "pw-new", out _)
            && _accountStore.Authenticate(rememberLogin, "Token000005x", out _) == AccountStore.LoginResult.WrongPassword;
        string? previousGms = Environment.GetEnvironmentVariable("HUXLEY_GM_LOGINS");
        Environment.SetEnvironmentVariable("HUXLEY_GM_LOGINS", rememberLogin);
        bool gmRemembered = _accountStore.Remember(rememberUser, "GmToken0001x")
            && _accountStore.Authenticate(rememberLogin, "GmToken0001x", out _) == AccountStore.LoginResult.Ok;
        Environment.SetEnvironmentVariable("HUXLEY_GM_LOGINS", previousGms);
        Check(unknownToken && stored && tokenLogin && passwordStillWorks && bannedToken && forgotten && oldestDropped
            && resetClears && gmRemembered,
            "remembered logins: hashed token stands in for the password, ban still applies, forget and a password "
            + "reset revoke it, at most five per account, GM logins too");
        Check(Lunarite.Handlers.ChatHandler.IsPlayedCommand(" /Played ") && Lunarite.Handlers.ChatHandler.IsPlayedCommand("!played")
            && !Lunarite.Handlers.ChatHandler.IsPlayedCommand("/played now")
            && Lunarite.Handlers.ChatHandler.FormatPlayed(0) == "0 h 0 min"
            && Lunarite.Handlers.ChatHandler.FormatPlayed(3 * 3600 + 25 * 60 + 59) == "3 h 25 min"
            && Lunarite.Handlers.ChatHandler.FormatPlayed(2 * 86400 + 5 * 3600 + 60) == "2 d 5 h 1 min",
            "/played answers the time played as days, hours and minutes");
        var relayReport = new P2PNetworkAddressesReport();
        relayReport.Entries.Add(new P2PNetworkAddressesReport.Entry(0x0100007F, 51405, 0x0100007F, 7777));
        var relayPublic = new P2PNetworkAddressesReport();
        relayPublic.Entries.Add(new P2PNetworkAddressesReport.Entry(0x0100007F, 51405, 0x0A0A0A0A, 40001));
        Check(P2PManager.ReportedPublicEndpoint(new P2PNatEndpointReport { PublicAddress = 0x0302A8C0, PublicPort = 7778 }, relayPublic)
                == (0x0302A8C0u, (ushort)7778)
            && P2PManager.ReportedPublicEndpoint(null, relayPublic) == (0x0A0A0A0Au, (ushort)40001)
            && P2PManager.ReportedPublicEndpoint(null, relayReport) == null
            && P2PManager.ReportedPublicEndpoint(null, null) == null,
            "listen-host public endpoint prefers op155, then a non-loopback op196 relay observation");
        Check(SimulationManager.IsSimulationSpecies(1) && SimulationManager.IsSimulationSpecies(4) && !SimulationManager.IsSimulationSpecies(0)
            && !SimulationManager.IsSimulationSpecies(5) && SimulationManager.IsSimulationSquad(4) && !SimulationManager.IsSimulationSquad(5)
            && SimulationManager.IsSimulationPlayerState(0) && SimulationManager.IsSimulationPlayerState(4) && !SimulationManager.IsSimulationPlayerState(3),
            "Simulation room species, squad and busy state accept only the client's ETribeType, MAX_SQUAD_NUM and SC_PLAYER_STATE values");
    }
}

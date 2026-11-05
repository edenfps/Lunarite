using Lunarite.Packets.Outgoing;
using Lunarite.Storage;

internal sealed partial class TestSuite
{
    private void GmAccountStatus()
    {
        var gmStatusCharacter = new CharacterStore.Character { Pid = 920777, OwnerUserId = _gmStatusUserId };
        string? previousGmLogins = Environment.GetEnvironmentVariable("HUXLEY_GM_LOGINS");
        Environment.SetEnvironmentVariable("HUXLEY_GM_LOGINS", "gmstatustest");
        byte gmStatus = _characterManager.UserStatusFor(gmStatusCharacter);
        Environment.SetEnvironmentVariable("HUXLEY_GM_LOGINS", "someone-else");
        byte normalStatus = _characterManager.UserStatusFor(gmStatusCharacter);
        Environment.SetEnvironmentVariable("HUXLEY_GM_LOGINS", previousGmLogins);
        Check(gmStatus == 100 && normalStatus == PlayerInfo.NormalUserStatus
            && _characterManager.UserStatusFor(null) == PlayerInfo.NormalUserStatus,
            "GM accounts get native nUserStatus 100 (client IsAdmin threshold); others stay normal");
    }
}

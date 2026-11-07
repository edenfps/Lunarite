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

        Environment.SetEnvironmentVariable("HUXLEY_GM_LOGINS", "gmstatustest");
        gmStatusCharacter.Name = "Eden";
        string gmTag = _characterManager.DisplayNameFor(gmStatusCharacter);
        Environment.SetEnvironmentVariable("HUXLEY_GM_LOGINS", "someone-else");
        string plainTag = _characterManager.DisplayNameFor(gmStatusCharacter);
        Environment.SetEnvironmentVariable("HUXLEY_GM_LOGINS", previousGmLogins);
        Check(gmTag == "[GM] Eden" && plainTag == "Eden" && _characterManager.DisplayNameFor(null) == "",
            "GM characters show to other players as [GM] Name; everyone else keeps the plain name");
        Check(CharacterStore.SameName("Eden", "[GM] Eden") && CharacterStore.SameName("Eden", "[gm]Eden")
            && CharacterStore.SameName("Eden", " eden ") && !CharacterStore.SameName("Eden", "[GM] Edenx")
            && !CharacterStore.SameName("Eden", "GM Eden"),
            "names typed from a GM tag still find the character, with or without the [GM] prefix");
    }
}

using System.Net;

using Lunarite.Packets.Common;
using Lunarite.Storage;

internal sealed partial class TestSuite
{
    // A stray Connect or a same-address collision spoils at most one handoff: the owners' retries work.
    private void TicketProbeIsBounded()
    {
        var probed = IPAddress.Parse("192.0.2.60");
        Check(!_accountSessionStore.TryClaim(probed, out _), "a Connect with no ticket is refused");
        _accountSessionStore.Issue(probed, 958840001);
        Check(_accountSessionStore.TryClaim(probed, out int retried) && retried == 958840001,
            "a Connect that found no ticket leaves the owner's later handoff on that address claimable");

        var family = IPAddress.Parse("192.0.2.61");
        _accountSessionStore.Issue(family, 958840002);
        _accountSessionStore.Issue(family, 958840003);
        bool collided = !_accountSessionStore.TryClaim(family, out _) && !_accountSessionStore.TryClaim(family, out _);
        _accountSessionStore.Issue(family, 958840002);
        bool firstRetry = _accountSessionStore.TryClaim(family, out int first) && first == 958840002;
        _accountSessionStore.Issue(family, 958840003);
        Check(collided && firstRetry && _accountSessionStore.TryClaim(family, out int second) && second == 958840003,
            "two accounts behind one address that collide both get in on retry");
    }

    private void LoginLimitsPerNameAndAddress()
    {
        var attacker = IPAddress.Parse("192.0.2.70");
        var neighbour = IPAddress.Parse("192.0.2.71");
        // Parallel sockets: reservations are taken before hashing, so none can exceed the limit.
        var reserved = new List<AccountSessionStore.LoginAttempt>();
        for (int i = 0; i < AccountSessionStore.MaxLoginFailuresPerLogin; i++)
            if (_accountSessionStore.TryBeginLogin(attacker, "LimitVictim", out var attempt)) reserved.Add(attempt!);
        bool overshoot = _accountSessionStore.TryBeginLogin(neighbour, "limitvictim", out _);
        Check(reserved.Count == AccountSessionStore.MaxLoginFailuresPerLogin && !overshoot,
            "wrong logins are limited per account name, across addresses, before any password is hashed");
        _accountSessionStore.EndLogin(reserved[0], failed: false);
        Check(_accountSessionStore.TryBeginLogin(neighbour, "limitvictim", out var freed)
            && _accountSessionStore.TryBeginLogin(attacker, "someoneelse", out _),
            "a login that was not a failure gives its slot back, and other names on the address still log in");
        _accountSessionStore.EndLogin(freed!, failed: false);

        var shared = IPAddress.Parse("192.0.2.72");
        int allowed = 0;
        for (int i = 0; i < AccountSessionStore.MaxLoginFailuresPerAddress + 1; i++)
            if (_accountSessionStore.TryBeginLogin(shared, $"limitname{i}", out _)) allowed++;
        Check(AccountSessionStore.MaxLoginFailuresPerAddress > AccountSessionStore.MaxLoginFailuresPerLogin
            && allowed == AccountSessionStore.MaxLoginFailuresPerAddress,
            "one address has a looser cap on wrong logins than one account name");
    }

    private void RegistrationLimits()
    {
        Check(_accountStore.TryRegister("limittaken", "secret", out _)
            && _accountStore.Register("LimitTaken", "other", out _, out bool hashed) == AccountStore.RegistrationResult.Duplicate
            && !hashed,
            "op6 with a taken login answers Duplicate without hashing the password");
        var cafe = IPAddress.Parse("192.0.2.80");
        int reserved = 0;
        while (reserved < 100 && _accountSessionStore.TryBeginRegistration(cafe, out var reservation))
        {
            reserved++;
            if (reserved == 1) _accountSessionStore.CancelRegistration(cafe, reservation);
        }
        Check(AccountSessionStore.MaxRegistrations >= 20 && reserved == AccountSessionStore.MaxRegistrations + 1,
            "a shared address can register about 20 accounts an hour, and an unhashed attempt is not counted");
    }

    private void GmLoginBootstrap()
    {
        string? previous = Environment.GetEnvironmentVariable("HUXLEY_GM_LOGINS");
        Environment.SetEnvironmentVariable("HUXLEY_GM_LOGINS", "bootgm,gmone");
        try
        {
            bool missing = _accountStore.MissingGmLogins("bootgm").Contains("bootgm");
            bool created = _accountStore.TryResetPassword("bootgm", "operator", out int gmUser) && gmUser > 0
                && _accountStore.TryLogin("bootgm", "operator", out int loggedIn) && loggedIn == gmUser;
            Check(missing && created && _accountStore.MissingGmLogins("bootgm").Count == 0
                && !_accountStore.TryResetPassword("notlistedmissing", "operator", out _)
                && _accountStore.Authenticate("gmone3notlisted", "x", out _) == AccountStore.LoginResult.NoneMember,
                "the operator reset creates a missing listed GM login; unlisted missing logins are not created");
        }
        finally { Environment.SetEnvironmentVariable("HUXLEY_GM_LOGINS", previous); }
    }

    private void CharacterNamesFollowClientEditBox()
    {
        const int owner = 958850001;
        Check(_characterStore.TryCreate(owner, "Ed-en", 1, new Custom(), out _) == CharacterStore.CreateOk
            && _characterStore.TryCreate(owner, "Dogma", 1, new Custom(), out _) == CharacterStore.CreateOk
            && _characterStore.TryCreate(owner, "[GM]Nova", 1, new Custom(), out _) == CharacterStore.CreateWrongName
            && _characterStore.TryCreate(owner, "Admin", 1, new Custom(), out _) == CharacterStore.CreateWrongName
            && _characterStore.TryCreate(owner, "Ed\u0001n", 1, new Custom(), out _) == CharacterStore.CreateWrongName,
            "names the client edit box allows are accepted; control characters and staff words are not");
    }
}

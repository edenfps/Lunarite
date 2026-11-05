using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Incoming;
using Lunarite.Storage;
using Lunarite.World;

internal sealed partial class TestSuite
{
    // op181 quest mail carries no unique ID from Kismet, so it needs its own throttle and daily limit.
    private void ScriptedMailBounds()
    {
        var character = _characterStore.Create(920700, "MailBound920700", 1, new Custom());
        int pid = character.Pid;
        _transmitStore.DeleteCharacter(pid);
        _transmitManager._scriptedMailSends.Remove(pid);
        using (var socket = new AuditGameSocket(pid, _loggerFactory))
        {
            socket.Client.Player = _playerManager.Get(pid);
            byte[] QuestMail()
            {
                using var wire = new PacketWriter();
                wire.Write((byte)181); wire.Write((byte)3); wire.Write("Polly"); wire.Write(character.Name);
                wire.Write("Orders"); wire.Write("body"); wire.Write(0);
                return socket.Request(wire.Buffer, 201);
            }
            var burst = Enumerable.Range(0, TransmitManager.ScriptedMailBurst).Select(_ => QuestMail()[1]).ToArray();
            var flood = QuestMail();
            Check(burst.All(x => x == TransmitResultCode.Ok) && flood[1] == TransmitResultCode.Error
                && _transmitStore.List(pid).Count == TransmitManager.ScriptedMailBurst,
                "op181 quest mail with ID 0 is delivered in a party-sized burst, then throttled per sender");
        }

        // The per-recipient daily limit counts stored scripted mail of today only.
        _transmitStore.DeleteCharacter(pid);
        _transmitManager._scriptedMailSends.Remove(pid);
        long day = TransmitManager.UtcDay();
        for (int i = 0; i < TransmitStore.ScriptedMailDailyLimit; i++)
            _transmitStore.AddScripted(pid, 3, "Polly", "Orders", "body", 0, day);
        var request = new TransmitSendSystemRequest { TransmitType = 3, FromName = "Polly", ToName = character.Name,
            Title = "Orders", Content = "body" };
        var overDaily = _transmitManager.TransmitSendSystem(pid, pid, request, 0, day);
        Check(!overDaily.Created && overDaily.Answer == TransmitResultCode.Error
            && _transmitStore.List(pid).Count == TransmitStore.ScriptedMailDailyLimit,
            "op181 quest mail past the recipient's daily limit is refused and stores nothing");
        _transmitStore.AddScripted(pid, 3, "Polly", "Orders", "body", 920701, day - 1);
        request.Id = 920701;
        var duplicate = _transmitManager.TransmitSendSystem(pid, pid, request, 1_000_000, day);
        Check(!duplicate.Created && duplicate.Answer == TransmitResultCode.Duplicate,
            "a repeated op181 unique ID still answers TRANSMITRET_DUPLICATE at the daily limit");
        request.Id = 0;
        var nextDay = _transmitManager.TransmitSendSystem(pid, pid, request, 2_000_000, day + 1);
        Check(nextDay.Created && nextDay.Answer == TransmitResultCode.Ok,
            "the op181 daily limit resets on the next UTC day");
        _transmitStore.DeleteCharacter(pid);
        _transmitManager._scriptedMailSends.Remove(pid);
    }

    // The money cap check and the credit share one lock, so concurrent credits never truncate.
    private void AtomicMoneyCredit()
    {
        const int pid = 920710;
        ResetCharacter(pid);
        var progress = _playerStore.GetProgress(pid);
        progress.Money = PlayerStore.MaxMoney - 10_500;
        int credited = 0;
        Parallel.For(0, 64, _ => { if (_playerStore.TryAddMoney(pid, 1000)) Interlocked.Increment(ref credited); });
        Check(credited == 10 && progress.Money == PlayerStore.MaxMoney - 500,
            "concurrent TryAddMoney credits are whole or refused, never truncated at the money cap");
        Check(!_playerStore.TryAddMoney(pid, 501) && _playerStore.TryAddMoney(pid, 500)
            && progress.Money == PlayerStore.MaxMoney,
            "TryAddMoney refuses a credit past the cap and accepts one landing exactly on it");

        // A CR mail claim the cap refuses leaves the CR on the mail.
        _transmitStore.DeleteCharacter(pid);
        var mail = _transmitStore.Add(pid, 0, 4, "System", "Pay", "", cr: 1);
        var claim = _transmitManager.TransmitGetCR(pid, mail.Sequence);
        Check(claim.Answer == TransmitResultCode.Error && _transmitStore.Get(pid, mail.Sequence)?.CR == 1
            && progress.Money == PlayerStore.MaxMoney,
            "a CR claim past the money cap is refused and keeps its CR");
        progress.Money = 0;
        claim = _transmitManager.TransmitGetCR(pid, mail.Sequence);
        Check(claim.Answer == TransmitResultCode.Ok && progress.Money == 1
            && _transmitStore.Get(pid, mail.Sequence)?.CR == 0,
            "the same CR claim succeeds once there is room");
        _transmitStore.DeleteCharacter(pid);
    }
}

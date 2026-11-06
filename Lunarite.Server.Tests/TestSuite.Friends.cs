using Lunarite.Packets.Outgoing;

internal sealed partial class TestSuite
{
    private void FriendsAndBlocks()
    {
        var friendList = new FriendList();
        friendList.Entries.Add(new FriendWireEntry(0, 7, "A", 1, 2, 3, 1));
        Check(friendList.Serialize().SequenceEqual(new byte[] { 160, 5, 1, 0, 7, 0, 0, 0,
                65, 0, 2, 3, 1, 1 })
            && new FriendActionResult { Opcode = 158, Result = 0 }.Serialize().SequenceEqual(new byte[] { 158, 0 })
            && new FriendActionResult { Opcode = 159, Result = 4 }.Serialize().SequenceEqual(new byte[] { 159, 4 }),
            "friend list/add/delete packets match native bdsbbbb and result layouts");
        _friendStore.Delete(920020, 920021);
        Check(_friendStore.Add(920020, 920021, 0) == 0
            && _friendStore.Add(920020, 920021, 0) == 2
            && _friendStore.List(920020).Any(x => x.Pid == 920021),
            "friend addition persists and reports the retail duplicate result");
        Check(_friendStore.Delete(920020, 920021) == 4
            && _friendStore.List(920020).All(x => x.Pid != 920021),
            "friend deletion persists and reports FRIEND_DELETE_SUCCESS");
        var blockList = new BlockPlayerList { Result = 0 };
        blockList.Entries.Add(new BlockWireEntry(7, "A", 2, 3, 1));
        Check(blockList.Serialize().SequenceEqual(new byte[] { 209, 0, 1, 7, 0, 0, 0, 65, 0, 2, 3, 1 })
            && new BlockActionResult { Opcode = 210, Result = 0 }.Serialize().SequenceEqual(new byte[] { 210, 0 })
            && new BlockActionResult { Opcode = 211, Result = 3 }.Serialize().SequenceEqual(new byte[] { 211, 3 }),
            "blocked-player list/add/delete packets match native dsbbb and result layouts");
        _blockStore.Delete(920022, 920023);
        Check(_blockStore.Add(920022, 920023) == 0 && _blockStore.Add(920022, 920023) == 5
            && _blockStore.Delete(920022, 920023) == 0,
            "blocked-player persistence reports native success and duplicate result values");
        _blockStore.Delete(920024, 920025);
        Check(_transmitManager.CanSendTransmit(920024, 920025)
            && _blockStore.Add(920025, 920024) == 0
            && !_transmitManager.CanSendTransmit(920024, 920025)
            && _transmitManager.CanSendTransmit(920025, 920024),
            "mail honors the recipient's directional block list with TRANSMITRET_BLOCKEDPLAYER");
        _blockStore.Delete(920025, 920024);
        Check(_transmitManager.ResolveTransmitRecipient(_ownedA.Pid, _ownedB.Name)?.Pid == _ownedB.Pid
            && _transmitManager.ResolveTransmitRecipient(0, _ownedB.Name.ToUpperInvariant())?.Pid == _ownedB.Pid
            && _transmitManager.ResolveTransmitRecipient(_ownedA.Pid, "Unknown Mail Recipient") == null
            && _transmitManager.ResolveTransmitRecipient(_ownedA.Pid, "") == null,
            "mail send resolves the typed recipient, never the UI player index as a character PID");
    }

    private void FriendCleanupSetup()
    {
        _friendStore.DeleteCharacter(DeletedPid);
        _friendStore.DeleteCharacter(SurvivingPid);
        _friendStore.Add(DeletedPid, SurvivingPid, 0);
        _friendStore.Add(SurvivingPid, DeletedPid, 0);
        _blockStore.DeleteCharacter(DeletedPid);
        _blockStore.DeleteCharacter(SurvivingPid);
        _blockStore.Add(DeletedPid, SurvivingPid);
        _blockStore.Add(SurvivingPid, DeletedPid);
    }
}

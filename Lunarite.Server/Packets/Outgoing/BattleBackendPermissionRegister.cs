namespace Lunarite.Packets.Outgoing;

// Game backend -> dedicated CServerTcpLink opcode 4. Native dispatch delegates to
// the same sub_111C0920 MMOPermission parser used by client opcode 55.
public sealed class BattleBackendPermissionRegister : QuestPermissionRegister
{
    protected override byte Opcode => 4;
    protected override bool CarriesQuestBackup => false;
}

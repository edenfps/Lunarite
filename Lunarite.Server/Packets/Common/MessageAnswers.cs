namespace Lunarite.Packets.Common;

// Answers the client shows as a localized message indexed by the answer byte, with no enum in the
// exported scripts. Names follow the INT strings (HuxleyMMOGame.int).

// Squad answers (op101-op107): SquadMsg[answer]. The squad sign-up list (LFS) answers use the
// same codes for 0-17 (LFSMsg).
public static class SquadAnswer
{
    public const byte Ok = 0;
    public const byte Error = 1;
    public const byte NoCommandLicense = 2;
    public const byte NotFound = 3;
    public const byte PlayerNotFound = 4;
    public const byte AlreadyFormed = 5;
    public const byte AlreadyRegistered = 6;
    public const byte NotInSquad = 7;
    public const byte OtherSquad = 8;
    public const byte NotEnoughMembers = 9;
    public const byte TooManyMembers = 10;
    public const byte EmptyArmorSlot = 11;
    public const byte EmptyWeaponSlot = 12;
    public const byte ZeroDurability = 13;
    public const byte MustBeInSquad = 14;
    public const byte MustNotBeInSquad = 15;
    public const byte LoadingRoomFailed = 16;
    public const byte NotEnoughBattleMedals = 17;
    public const byte EmptyName = 18;
    public const byte InvalidName = 19;
    public const byte Busy = 20;
}

// op31 battlefield entry answers: BattleMsg[answer].
public static class BattleAnswer
{
    public const byte Ok = 0;
    public const byte Error = 1;
    public const byte LevelTooLow = 2;
    public const byte NoCombatYet = 3;
    public const byte PlayerNotFound = 4;
    public const byte Full = 5;
    public const byte AlreadyParticipating = 6;
    public const byte NoCombatInProgress = 7;
    public const byte CombatInProgress = 8;
    public const byte NotEnoughMembers = 9;
    public const byte FieldFull = 10;
    public const byte ArmorUnequipped = 11;
    public const byte WeaponUnequipped = 12;
    public const byte ZeroDurability = 13;
    public const byte MustBeInSquad = 14;
    public const byte LeaveSquadPrompt = 15;
    public const byte LoadingRoomFailed = 16;
    public const byte NotEnoughBattleMedals = 17;
    public const byte WrongZone = 18;
    public const byte ParticipatingHere = 19;
    public const byte DoubleBooking = 20;
    public const byte NotOnWaitingList = 21;
    public const byte WaitingListFull = 22;
    public const byte NonCombatArea = 23;
    public const byte NotCombatHour = 24;
}

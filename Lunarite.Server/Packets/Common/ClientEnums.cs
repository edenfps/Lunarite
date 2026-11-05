namespace Lunarite.Packets.Common;

// EINVENTORY: Inventory a GenericItem lives in (bInventoryIndex).
public static class InventoryType
{
    public const byte Main = 0;
    public const byte Belt = 1;
    public const byte Vehicle = 2;
    public const byte Weapon = 3;
    public const byte Armor = 4;
    public const byte Bank = 5;
    public const byte Skill = 6;
    public const byte End = 7;
}

// ENUM_RESULTTYPE_ITEM: Item, vendor, inventory and bank answers.
public static class ItemResultCode
{
    public const byte Ok = 0;
    public const byte Error = 1;
    public const byte InvenFull = 2;
    public const byte Notexist = 3;
    public const byte ArmorOutofbound = 4;
    public const byte WeaponOutofbound = 5;
    public const byte InvenOutofbound = 6;
    public const byte InvalidItemid = 7;
    public const byte InvalidInventory = 8;
    public const byte CellNotEmpty = 9;
    public const byte NotSkillItem = 10;
    public const byte Undefined = 11;
    public const byte WrongPos = 12;
    public const byte NotEnoughEnergy = 13;
    public const byte NotEnoughMoney = 14;
    public const byte Class = 15;
    public const byte SkillNotEquipped = 16;
    public const byte SkillSlotNotFound = 17;
    public const byte SkillSlotInvalid = 18;
    public const byte SkillWrongPos = 19;
    public const byte OkSkillDecoupled = 20;
    public const byte NoUpbody = 21;
    public const byte WeaponLicense = 22;
    public const byte WrongParameter = 23;
    public const byte BankOpen = 24;
    public const byte BankClose = 25;
    public const byte BankInsert = 26;
    public const byte BankTakeout = 27;
    public const byte BankFull = 28;
    public const byte InsertProtion = 29;
    public const byte UsingInven = 30;
    public const byte SwapMerge = 31;
    public const byte OkDataOnly = 32;
    public const byte NotEnoughMedal = 33;
    public const byte Blocked = 34;
    public const byte BlockedAnother = 35;
    public const byte BankBlocked = 36;
    public const byte InvenBlocked = 37;
    public const byte MaterialBlocked = 38;
    public const byte Alreadylearnedrecipe = 39;
    public const byte NotEnoughPlayerlevel = 40;
    public const byte NotEnoughPlayerrank = 41;
    public const byte Cannotsell = 42;
    public const byte Maxmoney = 43;
    public const byte NoMoreExpand = 44;
    public const byte NoBattlescore = 45;
    public const byte CantUseitem = 46;
    public const byte FailedEvaluate = 47;
    public const byte PcroomItem = 48;
    public const byte TargetPcroomItem = 49;
}

// ENUM_RESULTTYPE_LICENSE: License acquire answers.
public static class LicenseResultCode
{
    public const byte Ok = 0;
    public const byte Error = 1;
    public const byte AlreadyHave = 2;
    public const byte PreLicense = 3;
    public const byte Class = 4;
    public const byte Lp = 5;
    public const byte WrongStyle = 6;
    public const byte NotExist = 7;
    public const byte NotHave = 8;
    public const byte Notenoughlevel = 9;
}

// ENUM_ANSWER: Yes/no answers to prompts (squad applications, invitations).
public static class ConsentAnswer
{
    public const byte Consent = 0;
    public const byte Refuse = 1;
    public const byte Exclusion = 2;
}

// ENUM_RESULTTYPE_QUEST: Quest acquire, complete, share and entry answers (QuestMessage[answer]).
public static class QuestResultCode
{
    public const byte Ok = 0;
    public const byte Error = 1;
    public const byte Notqualification = 2;
    public const byte Notfound = 3;
    public const byte NotfoundPlayer = 4;
    public const byte Fullplayer = 5;
    public const byte Sameplayer = 6;
    public const byte None = 7;
    public const byte Already = 8;
    public const byte Deficiency = 9;
    public const byte Exceed = 10;
    public const byte ArmorUnequipped = 11;
    public const byte WeaponUnequipped = 12;
    public const byte WeaponDuraZero = 13;
    public const byte Squadmode = 14;
    public const byte Singlemode = 15;
    public const byte FailLoadingroom = 16;
    public const byte NotfoundMedal = 17;
    public const byte RestrictedWeapon = 18;
    public const byte RestrictedArmor = 19;
    public const byte RestrictedGoods = 20;
    public const byte Alreadyacquire = 21;
    public const byte Impossible = 22;
    public const byte Notacquire = 23;
    public const byte Maxhave = 24;
    public const byte NoneSquad = 25;
    public const byte NotSquadleader = 26;
    public const byte NoneShare = 27;
    public const byte WrongRallypoint = 28;
    public const byte WrongObjective = 29;
    public const byte InvenFull = 30;
    public const byte AlreadyComplete = 31;
    public const byte Notacquiredplayer = 32;
    public const byte MismatchLevelclass = 33;
    public const byte MismatchRallypoint = 34;
    public const byte DonthaveItem = 35;
    public const byte DonthaveEnterItem = 36;
    public const byte DontSelectItem = 37;
    public const byte FarFromOwner = 38;
}

// CLAN_PROCESS_MSG: Clan request answers.
public static class ClanResultCode
{
    public const byte CreateSuccess = 0;
    public const byte CreateInvalidclass = 1;
    public const byte CreateShortmoney = 2;
    public const byte CreateExist = 3;
    public const byte CreateDuplicatename = 4;
    public const byte CreateValidname = 5;
    public const byte JoinSuccess = 6;
    public const byte InviteSuccess = 7;
    public const byte InviteOvermax = 8;
    public const byte InviteCanceled = 9;
    public const byte InviteNoexistmember = 10;
    public const byte SecedeSuccess = 11;
    public const byte SecedePreentrust = 12;
    public const byte MemberSuccess = 13;
    public const byte InfoWarsuccess = 14;
    public const byte InfoWarfailed = 15;
    public const byte InfoSuccess = 16;
    public const byte InfoFailed = 17;
    public const byte NoticeInvalid = 18;
    public const byte NoticeSuccess = 19;
    public const byte DuplicateExist = 20;
    public const byte DuplicateNoexist = 21;
    public const byte FireNoexist = 22;
    public const byte FireSuccess = 23;
    public const byte FireFired = 24;
    public const byte FireUnable = 25;
    public const byte EntrustSuccess = 26;
    public const byte EntrustUnable = 27;
    public const byte EntrustEntrusted = 28;
    public const byte EntrustNewmaster = 29;
    public const byte MarkSuccess = 30;
    public const byte MarkShortmoney = 31;
    public const byte MarkInvalid = 32;
    public const byte AppointSuccess = 33;
    public const byte AppointAppointed = 34;
    public const byte AppointMaxover = 35;
    public const byte AppointExit = 36;
    public const byte AppointInfoSuccess = 37;
    public const byte PermissonSuccess = 38;
    public const byte PermissonInfoSuccess = 39;
    public const byte ContributionSuccess = 40;
    public const byte ContributionUsingInven = 41;
    public const byte ContributionInvaliditem = 42;
    public const byte ContributionInvalidcount = 43;
    public const byte ContributionNotexit = 44;
    public const byte ContributionImpeaching = 45;
    public const byte ChoiceUpveteran = 46;
    public const byte ChoiceDownveteran = 47;
    public const byte Invalidname = 48;
    public const byte Nojoined = 49;
    public const byte Joined = 50;
    public const byte Nopermission = 51;
    public const byte Invalidtribe = 52;
    public const byte Dberror = 53;
    public const byte Unknown = 54;
    public const byte RenameSuccess = 55;
    public const byte RenameFailed = 56;
    public const byte RenameExist = 57;
    public const byte Num = 58;
}

// FRIEND_PROCESS_MSG: Friend list answers.
public static class FriendResultCode
{
    public const byte AddSuccess = 0;
    public const byte AddUnexist = 1;
    public const byte AddExisted = 2;
    public const byte AddOvermax = 3;
    public const byte DeleteSuccess = 4;
    public const byte ListSuccess = 5;
    public const byte ListFailed = 6;
    public const byte Invalidname = 7;
    public const byte Dberror = 8;
    public const byte Unknown = 9;
    public const byte Num = 10;
}

// ENUM_RESULTTYPE_USEITEM: City item use answers.
public static class UseItemResultCode
{
    public const byte None = 0;
    public const byte Spray = 1;
    public const byte Craftrecipe = 2;
    public const byte Respawn = 3;
    public const byte Invenexpand = 4;
    public const byte Bankexpand = 5;
    public const byte Resetbattlescore = 6;
}

// ECityVehicleResult: City vehicle answers.
public static class VehicleResultCode
{
    public const byte Ok = 0;
    public const byte Mismatch = 1;
    public const byte Usinginven = 2;
    public const byte Alreadyrode = 3;
    public const byte Vehicleinvenfull = 4;
    public const byte Notvehicle = 5;
    public const byte Notfoundvehicle = 6;
    public const byte Maininvenfull = 7;
    public const byte Lackoffuel = 8;
}

// ELicenseResetResult: License reset answers.
public static class LicenseResetResultCode
{
    public const byte Ok = 0;
    public const byte Internalerror = 1;
    public const byte Wrongparameter = 2;
    public const byte Usinginven = 3;
    public const byte Notresetitem = 4;
    public const byte Mustunequipweapons = 5;
    public const byte Invenblocked = 6;
    public const byte Itemblocked = 7;
    public const byte Nolicense = 8;
}

// ENUM_RESULTTYPE_TUNEUP: Tune-up answers.
public static class TuneUpResultCode
{
    public const byte Ok = 0;
    public const byte Error = 1;
    public const byte Disabletuneupitem = 2;
    public const byte Fulltuneup = 3;
    public const byte Notenoughmaterial = 4;
    public const byte Notenoughmoney = 5;
    public const byte Notenoughdurability = 6;
    public const byte InvenBlocked = 7;
    public const byte ItemBlocked = 8;
    public const byte PcroomItem = 9;
}

// SIMULATION_CENTER_ANSWER: Simulation Center (Skirmish) room answers.
public static class SimulationAnswer
{
    public const byte Ok = 0;
    public const byte Error = 1;
    public const byte Full = 2;
    public const byte Squad = 3;
    public const byte NoWeapon = 4;
    public const byte NoShield = 5;
    public const byte WrongPassword = 6;
    public const byte LevelError = 7;
    public const byte NoReady = 8;
    public const byte PlayerProportion = 9;
    public const byte NoFullConn = 10;
    public const byte NoRelayServer = 11;
    public const byte NoRoom = 12;
    public const byte AlreadyStart = 13;
    public const byte IntrusionFailTime = 14;
    public const byte IntrusionFailScore = 15;
    public const byte IntrusionFailLoading = 16;
    public const byte InvenFull = 17;
    public const byte RoomFull = 18;
    public const byte UnableNetwork = 19;
    public const byte NotEnoughBandwidth = 20;
    public const byte NotEnoughHardware = 21;
    public const byte BrokenRoom = 22;
}

// enum_TRANSMIT_RESULT: Mail (transmit) answers.
public static class TransmitResultCode
{
    public const byte Ok = 0;
    public const byte Error = 1;
    public const byte Self = 2;
    public const byte UsingInven = 3;
    public const byte ItemBind = 4;
    public const byte ItemNotexist = 5;
    public const byte NotEnoughMoney = 6;
    public const byte NotfoundPlayer = 7;
    public const byte NotfoundPost = 8;
    public const byte InvenFull = 9;
    public const byte Duplicate = 10;
    public const byte Blockedplayer = 11;
    public const byte ItemBlocked = 12;
    public const byte CantEnclose = 13;
    public const byte Unknown = 14;
}

// ENUM_BLOCKPLAYER_SEND_RESULT: Block list answers.
public static class BlockResultCode
{
    public const byte Ok = 0;
    public const byte Error = 1;
    public const byte Invalidname = 2;
    public const byte Notexistplayer = 3;
    public const byte Maxsize = 4;
    public const byte Alreadyadded = 5;
}

// ENUM_CITYWARP: City warp answers.
public static class CityWarpResultCode
{
    public const byte Ok = 0;
    public const byte Error = 1;
    public const byte Notenoughmoney = 2;
}

// EChatType: Chat channel of a message (send/receive pairs).
public static class ChatType
{
    public const byte NormalSend = 0;
    public const byte NormalRecv = 1;
    public const byte ShoutSend = 2;
    public const byte ShoutRecv = 3;
    public const byte WhisperSend = 4;
    public const byte WhisperRecv = 5;
    public const byte WhisperReply = 6;
    public const byte SquadSend = 7;
    public const byte SquadRecv = 8;
    public const byte ClanSend = 9;
    public const byte ClanRecv = 10;
    public const byte ClanNotice = 11;
    public const byte BattleSystem = 12;
    public const byte BattleFellowDeath = 13;
    public const byte BattleEnemyDeath = 14;
    public const byte SocialActionSend = 15;
    public const byte SocialActionRecv = 16;
    public const byte Error = 17;
    public const byte System = 18;
    public const byte Notice = 19;
    public const byte Default = 20;
    public const byte TeamSend = 21;
    public const byte TeamRecv = 22;
    public const byte TradeSend = 23;
    public const byte TradeRecv = 24;
}

// PAYITEM_REQUEST_RESPONSE_TYPE: Cash-shop request/response subtype.
public static class PayItemRequest
{
    public const byte CashInfo = 0;
    public const byte Buy = 1;
    public const byte PresentItem = 2;
    public const byte QueryStorage = 3;
    public const byte PresentCash = 4;
    public const byte CanBuyPresent = 5;
    public const byte EventItemList = 6;
    public const byte UseItem = 7;
    public const byte ThrowoutItem = 8;
    public const byte RemainItemCount = 9;
    public const byte Version = 10;
    public const byte MileageUse = 11;
    public const byte MileageCharge = 12;
    public const byte MileageLiveCharge = 13;
    public const byte ItemSerialUpdate = 14;
    public const byte CanBuyPresentInfoPackage = 15;
    public const byte RollbackUseItem = 16;
    public const byte CanIOpenIngameshop = 17;
    public const byte Num = 18;
}

// _PAYITEM_PROCESS_ERROR_MSG_: Cash-shop process results.
public static class PayItemResultCode
{
    public const byte SystemConnection = 0;
    public const byte SystemPending = 1;
    public const byte SystemUnknown = 2;
    public const byte SystemHanModule = 3;
    public const byte SystemDb = 4;
    public const byte SystemExpired = 5;
    public const byte CashinfoFunctionFail = 6;
    public const byte CashinfoSuccess = 7;
    public const byte BuyNotenoughcashFail = 8;
    public const byte BuyMismatchGamepayitemFail = 9;
    public const byte BuyFunctionFail = 10;
    public const byte BuySuccess = 11;
    public const byte QueryStorageFunctionFail = 12;
    public const byte QueryStorageSuccess = 13;
    public const byte UseItemFunctionFail = 14;
    public const byte UseItemMismatchGamepayitemFail = 15;
    public const byte UseItemNotenoughspaceFail = 16;
    public const byte UseItemInsertFail = 17;
    public const byte UseItemSuccess = 18;
    public const byte UseItemRollbackFail = 19;
    public const byte UseItemRollbackSuccess = 20;
    public const byte MileageChargeFunctionFail = 21;
    public const byte MileageChargeSuccess = 22;
    public const byte MileageUseFunctionFail = 23;
    public const byte MileageUseSuccess = 24;
    public const byte MileageLiveChargeFunctionFail = 25;
    public const byte MileageLiveChargeSuccess = 26;
    public const byte PresentItemFunctionFail = 27;
    public const byte PresentItemSuccess = 28;
    public const byte PresentItemReceived = 29;
    public const byte PresentItemNotenoughcashFail = 30;
    public const byte PresentItemInvalidreceiverFail = 31;
    public const byte VersionSuccess = 32;
    public const byte CaniopeningameshopSuccess = 33;
    public const byte CaniopeningameshopFail = 34;
}

// EReserveStatus: Battlefield axis reservation status (copied as-is by native OnBattleAxisInfos).
public static class ReserveStatus
{
    public const byte Reservable = 0;
    public const byte Reserved = 1;
    public const byte Full = 2;
}

// ENUM_BUFF_RESULT_CODE: Personal buff answers.
public static class BuffResultCode
{
    public const byte Ok = 0;
    public const byte UndefinedSetter = 1;
    public const byte UndefinedBuff = 2;
    public const byte ExistHighlevel = 3;
    public const byte MaxBuffLevel = 4;
    public const byte Added = 5;
    public const byte Deleted = 6;
    public const byte IncrementLevel = 7;
    public const byte DecrementLevel = 8;
    public const byte CantUse = 9;
}

// ENUM_DRINK_RESULT_CODE: Pub drink answers.
public static class DrinkResultCode
{
    public const byte Ok = 0;
    public const byte UndefinedSetter = 1;
    public const byte NotEnoughMoney = 2;
}

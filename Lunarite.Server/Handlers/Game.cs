using System;
using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.Packets.Outgoing;
using Lunarite.World;

namespace Lunarite.Handlers;

[Handler(45406, HandlerType.Game)]
public static class Game
{
    private static ILogger _logger = null!;
    private static SimulationManager _simulationManager = null!;
    private static PlayerManager _playerManager = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(Game));
        _playerManager = serviceProvider.GetRequiredService<PlayerManager>();
        _simulationManager = serviceProvider.GetRequiredService<SimulationManager>();
    }

    internal const int DirectoryPageSize = byte.MaxValue; // native entry count is one byte

    public static void OnHandlePacket(Client client, ReadOnlySpan<byte> data)
    {
        var reader = new PacketReader(data);

        if (!reader.TryRead(out byte type))
        {
            _logger.LogError("Failed to read packet type. ( Data: {0} )", Convert.ToHexString(data));
            return;
        }

        if (client.Player == null && !IsAllowedBeforeSelection(type, client.Session.AccountId != null))
        {
            _logger.LogWarning("[Accounts] dropped op{0} from Game client {1} before character selection (account {2}).",
                type, client.Id, (client.Session.AccountId ?? 0));
            return;
        }

        var handled = type switch
        {
            0 => ConnectHandler.HandlePacket(client, ref reader),
            1 => ServerTimeAcknowledgedHandler.HandlePacket(client, ref reader),
            10 => VerifyHandler.HandlePacket(client, ref reader),
            11 => PlayerCreateHandler.HandlePacket(client, ref reader),
            12 => PlayerDeleteHandler.HandlePacket(client, ref reader),
            13 => PlayerRestoreHandler.HandlePacket(client, ref reader),
            14 => PlayerSelectHandler.HandlePacket(client, ref reader),
            15 => PlayerInfoViewHandler.HandlePacket(client, ref reader),
            16 => PossibleCreateCampHandler.HandlePacket(client, ref reader),
            17 => SpawnHandler.HandlePacket(client, ref reader),
            18 => MoveInputHandler.HandlePacket(client, ref reader),
            19 => MoveEventHandler.HandlePacket(client, ref reader),
            20 => MoveMapHandler.HandlePacket(client, ref reader),
            21 => ChatHandler.HandlePacket(client, ref reader),
            22 => AnimationHandler.HandlePacket(client, ref reader),
            23 => VisibleHandler.HandlePacket(client, ref reader),
            24 => ParameterHandler.HandlePacket(client, ref reader),
            25 => PositionHandler.HandlePacket(client, ref reader),
            26 => BattleReserveHandler.HandlePacket(client, ref reader),
            27 => BattleEnterHandler.HandlePacket(client, ref reader),
            28 => BattleLeaveHandler.HandlePacket(client, ref reader),
            29 => BattleIntrusionHandler.HandlePacket(client, ref reader),
            30 => BattleAxisInfosHandler.HandlePacket(client, ref reader),
            31 => BattleFieldInfosHandler.HandlePacket(client, ref reader),
            32 => QuestCheckHandler.HandlePacket(client, ref reader), // SendBattleQuestCheck (same db shape/validation)
            33 => QuestCheckHandler.HandlePacket(client, ref reader),
            34 => QuestDecisionAnswerHandler.HandlePacket(client, ref reader),
            35 => QuestPossibleListHandler.HandlePacket(client, ref reader),
            36 => QuestAcquireHandler.HandlePacket(client, ref reader),
            37 => QuestAcquireListHandler.HandlePacket(client, ref reader),
            38 => QuestCompletionListHandler.HandlePacket(client, ref reader),
            39 => QuestCreateHandler.HandlePacket(client, ref reader),
            40 => QuestEnterHandler.HandlePacket(client, ref reader),
            41 => QuestLeaveHandler.HandlePacket(client, ref reader),
            42 => QuestShareHandler.HandlePacket(client, ref reader),
            43 => QuestShareConsentHandler.HandlePacket(client, ref reader),
            44 => QuestRetireHandler.HandlePacket(client, ref reader),
            45 => QuestCompleteHandler.HandlePacket(client, ref reader),
            46 => QuestInsertItemHandler.HandlePacket(client, ref reader),
            47 => QuestInsertDropItemHandler.HandlePacket(client, ref reader),
            // Listen-server reports forwarded by the client hosting the P2P instance.
            48 => QuestInsertDropItemAtOnceHandler.HandlePacket(client, ref reader),
            50 => QuestReduceItemHandler.HandlePacket(client, ref reader),
            51 => QuestCreateAckHandler.HandlePacket(client, ref reader),
            52 => QuestJoinedPlayerStateHandler.HandlePacket(client, ref reader),
            54 => QuestListenRallyPointHandler.HandlePacket(client, ref reader),
            55 => QuestListenObjectiveHandler.HandlePacket(client, ref reader),
            56 => QuestEnemydownHandler.HandlePacket(client, ref reader),
            57 => DropItemGroupHandler.HandlePacket(client, ref reader),
            60 => QuestResultHandler.HandlePacket(client, ref reader),
            61 => QuestListenSpawnHandler.HandlePacket(client, ref reader),
            62 => DiagnosticsResultHandler.HandlePacket(client, ref reader),
            64 => BuyItemHandler.HandlePacket(client, ref reader),
            65 => SellItemHandler.HandlePacket(client, ref reader),
            66 => DropItemHandler.HandlePacket(client, ref reader),
            67 => EquipArmorHandler.HandlePacket(client, ref reader),
            68 => EquipWeaponHandler.HandlePacket(client, ref reader),
            69 => EquipBeltHandler.HandlePacket(client, ref reader),
            70 => TakeOffArmorHandler.HandlePacket(client, ref reader),
            71 => TakeOffWeaponHandler.HandlePacket(client, ref reader),
            72 => TakeOffBeltHandler.HandlePacket(client, ref reader),
            73 => EquipSkillHandler.HandlePacket(client, ref reader),
            74 => TakeOffSkillHandler.HandlePacket(client, ref reader),
            75 => UpdateSkillSlotHandler.HandlePacket(client, ref reader),
            76 => SwapItemInSameInvenHandler.HandlePacket(client, ref reader),
            77 => UseItemHandler.HandlePacket(client, ref reader),
            78 => TradeHandler.HandlePacket(client, ref reader),
            79 => InsertBankHandler.HandlePacket(client, ref reader),
            80 => TakeoutBankHandler.HandlePacket(client, ref reader),
            81 => RepairHandler.HandlePacket(client, ref reader),
            82 => CraftHandler.HandlePacket(client, ref reader),
            83 => RequestCraftRecipeHandler.HandlePacket(client, ref reader),
            84 => ItemArrangeHandler.HandlePacket(client, ref reader),
            85 => GetLicenseHandler.HandlePacket(client, ref reader),
            86 => ResetLicenseHandler.HandlePacket(client, ref reader),
            87 => SquadNotifyHandler.HandlePacket(client, ref reader),
            88 => SquadListHandler.HandlePacket(client, ref reader),
            89 => SquadMemberListHandler.HandlePacket(client, ref reader),
            90 => SquadCreateHandler.HandlePacket(client, ref reader),
            91 => SquadJoinHandler.HandlePacket(client, ref reader),
            92 => SquadSecessionHandler.HandlePacket(client, ref reader),
            93 => SquadSearchHandler.HandlePacket(client, ref reader),
            94 => SquadApplicationAnswerHandler.HandlePacket(client, ref reader),
            95 => DelegateLeaderPowerHandler.HandlePacket(client, ref reader),
            96 => VanSquadMemberHandler.HandlePacket(client, ref reader),
            97 => LFSListHandler.HandlePacket(client, ref reader),
            98 => LFSJoinHandler.HandlePacket(client, ref reader),
            99 => LFSSecessionHandler.HandlePacket(client, ref reader),
            100 => LFSSearchHandler.HandlePacket(client, ref reader),
            101 => LFSPlayerInfoHandler.HandlePacket(client, ref reader),
            102 => LFSInviteHandler.HandlePacket(client, ref reader),
            103 => LFSInviteAnswerHandler.HandlePacket(client, ref reader),
            104 => TransportEnterHandler.HandlePacket(client, ref reader),
            105 => TransportLeaveHandler.HandlePacket(client, ref reader),
            106 => BRTLeaveHandler.HandlePacket(client, ref reader),
            107 => BRTForcedStartHandler.HandlePacket(client, ref reader),
            108 => SimulationPermissionAckHandler.HandlePacket(client, ref reader),
            109 => SimulationJoinedPlayerStateHandler.HandlePacket(client, ref reader),
            114 => BattleResultHandler.HandlePacket(client, ref reader),
            115 => BattleResultSecretHandler.HandlePacket(client, ref reader),
            116 => ListenReduceItemHandler.HandlePacket(client, ref reader),
            117 => ErrorLogHandler.HandlePacket(client, ref reader),
            118 => P2PHolePunchAcknowledgmentHandler.HandlePacket(client, ref reader),
            119 => QuestStartHandler.HandlePacket(client, ref reader),
            120 => GotoCharMenuHandler.HandlePacket(client, ref reader),
            121 => EndTutorialHandler.HandlePacket(client, ref reader),
            122 => SCChannelListHandler.HandlePacket(client, ref reader),
            123 => JoinChannelHandler.HandlePacket(client, ref reader),
            124 => LeaveChannelHandler.HandlePacket(client, ref reader),
            125 => CreateRoomHandler.HandlePacket(client, ref reader),
            126 => JoinRoomHandler.HandlePacket(client, ref reader),
            127 => LeaveRoomHandler.HandlePacket(client, ref reader),
            128 => ReadyHandler.HandlePacket(client, ref reader),
            129 => ChangeRoomInfoHandler.HandlePacket(client, ref reader),
            130 => ChangeSquadHandler.HandlePacket(client, ref reader),
            131 => ReadyCancelHandler.HandlePacket(client, ref reader),
            132 => SimulPlayerStateBusyHandler.HandlePacket(client, ref reader),
            133 => ChangeSpeciesHandler.HandlePacket(client, ref reader),
            135 => FriendAddHandler.HandlePacket(client, ref reader),
            136 => FriendDelHandler.HandlePacket(client, ref reader),
            137 => FriendListHandler.HandlePacket(client, ref reader),
            138 => MoveMapHandler.HandlePacket(client, ref reader),
            139 => MoveMapReturnHandler.HandlePacket(client, ref reader),
            140 => QuickJoinRoomHandler.HandlePacket(client, ref reader),
            141 => StartMatchHandler.HandlePacket(client, ref reader),
            142 => KickOutHandler.HandlePacket(client, ref reader),
            143 => AuctionListHandler.HandlePacket(client, ref reader),
            144 => AuctionListMineHandler.HandlePacket(client, ref reader),
            146 => AuctionRegisterHandler.HandlePacket(client, ref reader),
            147 => AuctionRegisterCancelHandler.HandlePacket(client, ref reader),
            148 => AuctionBidHandler.HandlePacket(client, ref reader),
            149 => AuctionListTimeOutHandler.HandlePacket(client, ref reader),
            150 => AuctionReturnAllHandler.HandlePacket(client, ref reader),
            151 => AuctionCheckTimeOutHandler.HandlePacket(client, ref reader),
            // Native op152 is the same game-backup sender in quests and Simulation matches.
            152 => _simulationManager.IsInSimulationRoom(client) ? BattleGameDataHandler.HandlePacket(client, ref reader)
                : QuestGameDataHandler.HandlePacket(client, ref reader),
            155 => P2PNatEndpointReportHandler.HandlePacket(client, ref reader),
            156 => VehicleActionAddHandler.HandlePacket(client, ref reader),
            157 => VehicleActionRemoveHandler.HandlePacket(client, ref reader),
            158 => VehicleActionSummonHandler.HandlePacket(client, ref reader),
            159 => ClanCreateHandler.HandlePacket(client, ref reader),
            160 => ClanJoinHandler.HandlePacket(client, ref reader),
            161 => ClanSecedeHandler.HandlePacket(client, ref reader),
            162 => ClanInviteHandler.HandlePacket(client, ref reader),
            163 => ClanInfoHandler.HandlePacket(client, ref reader),
            164 => ClanMemberHandler.HandlePacket(client, ref reader),
            165 => ClanNoticeHandler.HandlePacket(client, ref reader),
            166 => ClanNoticeInfoHandler.HandlePacket(client, ref reader),
            167 => ClanDuplicateCheckHandler.HandlePacket(client, ref reader),
            168 => ClanFireHandler.HandlePacket(client, ref reader),
            169 => ClanMyInfoHandler.HandlePacket(client, ref reader),
            170 => ClanEntrustHandler.HandlePacket(client, ref reader),
            171 => ClanMarkHandler.HandlePacket(client, ref reader),
            172 => ClanAppointmentHandler.HandlePacket(client, ref reader),
            173 => ClanAppointmentInfoHandler.HandlePacket(client, ref reader),
            174 => ClanPermissionHandler.HandlePacket(client, ref reader),
            175 => ClanPermissionInfoHandler.HandlePacket(client, ref reader),
            176 => ClanContributionHandler.HandlePacket(client, ref reader),
            177 => UndoItemHandler.HandlePacket(client, ref reader),
            178 => TransmitListHandler.HandlePacket(client, ref reader),
            179 => TransmitContentHandler.HandlePacket(client, ref reader),
            180 => TransmitSendHandler.HandlePacket(client, ref reader),
            181 => TransmitSendSystemHandler.HandlePacket(client, ref reader),
            182 => TransmitModifyHandler.HandlePacket(client, ref reader),
            183 => TransmitGetItemHandler.HandlePacket(client, ref reader),
            184 => TransmitGetCRHandler.HandlePacket(client, ref reader),
            186 => GiveCityMedalHandler.HandlePacket(client, ref reader),
            189 => BlockPlayerListHandler.HandlePacket(client, ref reader),
            190 => BlockPlayerAddHandler.HandlePacket(client, ref reader),
            191 => BlockPlayerDeleteHandler.HandlePacket(client, ref reader),
            192 => TuneUpHandler.HandlePacket(client, ref reader),
            193 => BattleRecordHandler.HandlePacket(client, ref reader),
            194 => SimulationPingReportHandler.HandlePacket(client, ref reader),
            195 => CityWarpHandler.HandlePacket(client, ref reader),
            196 => P2PNetworkAddressesHandler.HandlePacket(client, ref reader),
            197 => PayItemHandler.HandlePacket(client, ref reader),
            198 => ResetTuneUpHandler.HandlePacket(client, ref reader),
            199 => QuestRoomNoticeHandler.HandlePacket(client, ref reader),
            200 => UsePersonalRespawnHandler.HandlePacket(client, ref reader),
            201 => ResetBindHandler.HandlePacket(client, ref reader),
            202 => GaugeBarGameResultHandler.HandlePacket(client, ref reader),
            203 => DeletePersonalBuffHandler.HandlePacket(client, ref reader),
            204 => BuyDrinkHandler.HandlePacket(client, ref reader),
            _ => false
        };

        if (!handled)
            _logger.LogWarning("Unhandled packet received. ( Type: {0}, Data: {1} )", type, Convert.ToHexString(data));
    }

    // Before PlayerSelect a link is on the select-player scene: Connect, the ServerTime ack,
    // Verify, roster edits and selection (10-17), client error/diagnostic reports, the NAT test
    // reports sent right after Connect, the char-menu return and the account-scoped cash shop.
    // Everything else needs a selected character; without one it would act as PID 0.
    public static bool IsAllowedBeforeSelection(byte type, bool authenticated) => type == 0
        || authenticated && type is 1 or (>= 10 and <= 17) or 62 or 117 or 118 or 120 or 155 or 196 or 197;

    internal static Player PlayerFor(int pid) => _playerManager.Get(pid);

    // 0 before selection.
    internal static int PidOf(Client client) => client.Player?.Pid ?? 0;

    internal static bool TryGetClient(int pid, [NotNullWhen(true)] out Client? client) =>
        _playerManager.TryGetClient(pid, out client);

    // OnLoadMap/OnPlayerList resume FP2PSocket's receive thread, which recvfrom()s the shared UDP
    // socket every 10 ms and consumes game packets as P2P control traffic. Only op42 OnQuestCreate
    // and op125 ProcHoleResult suspend it. op125 with a nonzero result keeps the direct route,
    // clears the probe state and calls SuspendRecvThread; the net driver then handles hole-punch
    // frames itself.
    internal static readonly HoleResult P2PDirectHoleResult = new() { Result = 1, Playable = 1 };
}

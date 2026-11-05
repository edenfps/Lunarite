using System.Buffers.Binary;
using System.Net;

using Lunarite.Handlers;
using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Outgoing;
using Lunarite.Resources;
using Lunarite.Storage;

internal sealed partial class TestSuite
{
    // Rally-end pickups with too little MAIN room. OnInsertDropItem_AtOnce_Quest wipes the reward
    // panel and refills it only on INSERT_PROTION (29) from the op51 list, so a full inventory must
    // insert what fits and hand back the rest instead of answering 1 and losing the whole run.
    private void PartialPickupInsert()
    {
        const int pid = 923401, clientId = 923402;
        const int stackItem = 604221, otherItem = 604019;
        int maxStack = _itemCatalog.Get(stackItem)?.MaxCollapseCount ?? 0;
        Check(maxStack >= 2 && _itemCatalog.Get(otherItem) != null,
            "partial-insert fixture items exist and the first one stacks");

        ResetCharacter(pid);
        _playerStore.GetProgress(pid).InvenExpandStep = 2;
        _playerStore.GetInventory(pid, () => Enumerable.Range(0, 77)
            .Select(i => new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = (byte)i, ItemId = 609301, Count = 1 })
            .ToList());
        QuestLootGrants.ClearInstance(pid);
        _questInstanceManager.SetHost(pid, pid);
        _questRoomMembers.SetInstance(clientId, pid);
        _questInstanceManager.LeaveQuestInstance(clientId, pid);
        QuestLootGrants.Issue(pid, pid, [(stackItem, maxStack + 1), (otherItem, 2)]);

        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var peer = new System.Net.Sockets.TcpClient();
        peer.Connect(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
        using var accepted = listener.AcceptTcpClient();
        listener.Stop();
        using var client = new Client(clientId, new Server(0, HandlerType.Game, static (_, _) => { }, _loggerFactory), accepted);
        peer.ReceiveTimeout = 3000;
        var stream = peer.GetStream();
        byte[] Read()
        {
            Span<byte> header = stackalloc byte[2];
            stream.ReadExactly(header);
            var payload = new byte[BinaryPrimitives.ReadUInt16LittleEndian(header) - 2];
            stream.ReadExactly(payload);
            for (int i = 0; i < payload.Length; i++) payload[i] = client.DecryptTable[0, payload[i]];
            return payload;
        }
        byte[] ReadUntil(byte opcode, out int inventoryInserts)
        {
            inventoryInserts = 0;
            for (int i = 0; i < 30; i++)
            {
                byte[] packet = Read();
                if (packet[0] == opcode) return packet;
                if (packet[0] == 82) inventoryInserts++;
            }
            throw new InvalidOperationException($"No op{opcode} was sent.");
        }
        int Held(int itemId) => _playerStore.GetInventory(pid, () => [])
            .Where(x => x.ItemId == itemId).Sum(x => x.Count);
        _ = Read(); // GameVersion, sent by the Client constructor.
        client.Player = _playerManager.Get(pid);

        // One free cell: the stackable line fills it, the rest of both lines stays uncollected.
        // The over-claim (maxStack + 5 against maxStack + 1 issued) is capped, not fatal.
        using (var batch = new PacketWriter())
        {
            batch.Write((byte)48); batch.Write(2);
            batch.Write(stackItem); batch.Write(maxStack + 5);
            batch.Write(otherItem); batch.Write(2);
            Game.OnHandlePacket(client, batch.Buffer);
        }
        byte[] batchAnswer = ReadUntil(51, out int batchInserts);
        Check(batchAnswer.SequenceEqual(new InsertDropItemAtOnceQuest
                { Answer = ItemResultCode.InsertProtion, Items = [(stackItem, 1), (otherItem, 2)] }.Serialize())
            && batchInserts == 1 && Held(stackItem) == maxStack && Held(otherItem) == 0,
            "op48 with too little room inserts what fits and answers INSERT_PROTION with only the leftovers");

        using (var single = new PacketWriter())
        {
            single.Write((byte)47); single.Write(otherItem); single.Write(2);
            Game.OnHandlePacket(client, single.Buffer);
        }
        Check(ReadUntil(50, out int singleInserts).SequenceEqual(new InsertDropItemQuest
                { Answer = ItemResultCode.InsertProtion, ItemIndex = otherItem, Quantity = 0 }.Serialize())
            && singleInserts == 0 && Held(otherItem) == 0,
            "op47 into a full inventory answers INSERT_PROTION with zero inserted so the client shows NotEnoghSlot");

        Check(QuestLootGrants.TryClaim(pid, pid, [(stackItem, 1), (otherItem, 2)], () => true)
            && !QuestLootGrants.TryClaim(pid, pid, [(stackItem, 1)], () => true),
            "pickups that did not fit stay claimable and inserted units are consumed exactly once");

        bool partialPool = QuestLootGrants.TryClaimUpTo(pid, pid, [(stackItem, 3)], clamp: false,
            _ => [3], out _);
        QuestLootGrants.Issue(pid, pid, [(stackItem, 3)]);
        bool partialCommit = QuestLootGrants.TryClaimUpTo(pid, pid, [(stackItem, 3)], clamp: false,
            _ => [1], out var partialClaims);
        Check(!partialPool && partialCommit && partialClaims.Single() == (stackItem, 3, 1)
            && QuestLootGrants.TryClaim(pid, pid, [(stackItem, 2)], () => true)
            && !QuestLootGrants.TryClaim(pid, pid, [(stackItem, 1)], () => true),
            "a partial pickup commit consumes only the units it inserted");
        QuestLootGrants.ClearInstance(pid);
        client.Player = null;
    }
}

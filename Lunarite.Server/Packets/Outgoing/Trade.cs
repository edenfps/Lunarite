using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// op85: one trade event per TradeAction.
public sealed class Trade : IPacketSerialize
{
    public byte Action;
    public int Pid;
    public string Name = "";
    public byte Result;
    public byte Slot;
    public int Money;
    public byte[] Item = [];

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)85); writer.Write(Action);
        switch (Action)
        {
            case TradeAction.Ask or TradeAction.Accept: writer.Write(Pid); writer.Write(Name); break;
            case TradeAction.Cancel or TradeAction.CancelAsk: writer.Write(Pid); break;
            case TradeAction.AddItem:
                writer.Write(Result);
                if (Result == TradeItemResult.Partner) { writer.Write(Slot); writer.Write(Item); }
                break;
            case TradeAction.SubItem:
                writer.Write(Result);
                if (Result == TradeItemResult.Partner) writer.Write(Slot);
                break;
            case TradeAction.SetMoney: writer.Write(Money); break;
            // The other actions have no body.
        }
        return writer.Buffer;
    }
}

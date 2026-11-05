namespace Lunarite.Packets.Outgoing;

// op85 actions: each one fires the client event it is named after (native OnTrade @0x11126EA0).
public static class TradeAction
{
    public const byte Ask = 0;                        // OnTradeAsk(pid, name)
    public const byte Accept = 1;                     // OnTradeAccept(pid, name)
    public const byte Cancel = 2;                     // OnTradeCancel(pid)
    public const byte Fail = 3;                       // OnTradeFail
    public const byte Busy = 4;                       // OnTradeBusy
    public const byte AddItem = 5;                    // see TradeItemResult
    public const byte SubItem = 6;                    // see TradeItemResult
    public const byte SetMoney = 7;                   // OnTradeSetMoney(money)
    public const byte AskCompletion = 8;              // OnTradeAskCompletion
    public const byte Completion = 9;                 // OnTradeCompletion
    public const byte CompletionCancel = 10;          // OnTradeCompletionCancel
    public const byte BlockPlayer = 11;               // OnTradeBlockPlayer
    public const byte CancelAsk = 12;                 // OnTradeCancelAsk(pid)
    public const byte InvenBlocked = 13;              // OnTradeInvenBlocked
    public const byte AnotherPlayerInvenBlocked = 14; // OnTradeAnotherPlayerInvenBlocked
}

using Lunarite.Packets.Outgoing;

internal sealed partial class TestSuite
{
    private void CityEventPackets()
    {
        _economyRates.ResetNeutral();
        Check(new ActiveEvent { Event = 12, Active = 1, Parameter = 34, Value1 = 56, Value2 = 78 }
                .Serialize().SequenceEqual(new byte[] { 4, 12, 0, 0, 0, 1, 34, 0, 0, 0,
                    56, 0, 0, 0, 78, 0, 0, 0 }),
            "server op4 serializes native event/active/parameter/value tuple");
        Check(new WorldEvent { Pid = 0x12345678, Event = 9, Value = 42 }.Serialize()
                .SequenceEqual(new byte[] { 117, 0x78, 0x56, 0x34, 0x12, 9, 42, 0, 0, 0 }),
            "server op117 serializes native PID/event/value body");
        var notice = new Message { Kind = 2, Text = "Ready", LifeSeconds = 5, Red = 10, Green = 20, Blue = 30 }.Serialize();
        Check(notice[0] == 116 && notice[1] == 2 && notice[^4..].SequenceEqual(new byte[] { 5, 10, 20, 30 }),
            "server op116 serializes native notice string/lifetime/RGB variant");
        Check(new CityCompulsionReturn().Serialize().SequenceEqual(new byte[] { 28 }),
            "server op28 invokes the native parameterless forced-city-return event");
        Check(new BattleInvite { Axis = 2, Area = 7 }.Serialize().SequenceEqual(new byte[] { 30, 2, 7 }),
            "server op30 serializes native battle axis/area invitation bytes");
        Check(new BattleOccupation { Server = 1, FromSpecies = 0, ToSpecies = 1 }.Serialize()
                .SequenceEqual(new byte[] { 34, 1, 0, 1 }),
            "server op34 serializes native server/from/to occupation bytes");
    }
}

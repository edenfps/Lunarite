namespace Lunarite.Packets.Outgoing;

public readonly record struct LfsEntry(int Pid, string Name, byte Level, byte LevelClass, byte Style);

using System;

namespace Lunarite.Network;

// Marks a static packet handler class. ConfigurePacketHandlers calls its
// ConfigureServices(IServiceProvider) at startup.
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class PacketHandlerAttribute : Attribute
{
}

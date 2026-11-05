using System;

namespace Lunarite.Network;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class HandlerAttribute : Attribute
{
    public const string ConfigureMethodName = "ConfigureServices";

    public int Port;
    public HandlerType Type;

    public HandlerAttribute(int port, HandlerType type)
    {
        Port = port;
        Type = type;
    }
}
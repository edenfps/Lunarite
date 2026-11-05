using Lunarite.Network;

namespace Lunarite.Packets.Common;

public class Custom : ISerializableType, IDeserializableType
{
    public byte Hair;

    public byte Color;

    public byte Skin;

    public byte Eye1;
    public byte Eye2;
    public byte Eye3;

    public byte Nose1;
    public byte Nose2;
    public byte Nose3;
    public byte Nose4;

    public byte Mouth1;
    public byte Mouth2;
    public byte Mouth3;

    public byte Jaws1;
    public byte Jaws2;

    public byte Face1;
    public byte Face2;

    public byte EyeBrow1;
    public byte EyeBrow2;
    public byte EyeBrow3;
    public byte EyeBrow4;

    public byte MakeUp;

    public byte Personality;
    public byte PersonalityX;
    public byte PersonalityY;

    public void Serialize(PacketWriter writer)
    {
        writer.Write(Hair);

        writer.Write(Color);

        writer.Write(Skin);

        writer.Write(Eye1);
        writer.Write(Eye2);
        writer.Write(Eye3);

        writer.Write(Nose1);
        writer.Write(Nose2);
        writer.Write(Nose3);
        writer.Write(Nose4);

        writer.Write(Mouth1);
        writer.Write(Mouth2);
        writer.Write(Mouth3);

        writer.Write(Jaws1);
        writer.Write(Jaws2);

        writer.Write(Face1);
        writer.Write(Face2);

        writer.Write(EyeBrow1);
        writer.Write(EyeBrow2);
        writer.Write(EyeBrow3);
        writer.Write(EyeBrow4);

        writer.Write(MakeUp);

        writer.Write(Personality);
        writer.Write(PersonalityX);
        writer.Write(PersonalityY);
    }

    public bool TryRead(ref PacketReader reader)
    {
        if (!reader.TryRead(out Hair))
            return false;

        if (!reader.TryRead(out Color))
            return false;

        if (!reader.TryRead(out Skin))
            return false;

        if (!reader.TryRead(out Eye1))
            return false;

        if (!reader.TryRead(out Eye2))
            return false;

        if (!reader.TryRead(out Eye3))
            return false;

        if (!reader.TryRead(out Nose1))
            return false;

        if (!reader.TryRead(out Nose2))
            return false;

        if (!reader.TryRead(out Nose3))
            return false;

        if (!reader.TryRead(out Nose4))
            return false;

        if (!reader.TryRead(out Mouth1))
            return false;

        if (!reader.TryRead(out Mouth2))
            return false;

        if (!reader.TryRead(out Mouth3))
            return false;

        if (!reader.TryRead(out Jaws1))
            return false;

        if (!reader.TryRead(out Jaws2))
            return false;

        if (!reader.TryRead(out Face1))
            return false;

        if (!reader.TryRead(out Face2))
            return false;

        if (!reader.TryRead(out EyeBrow1))
            return false;

        if (!reader.TryRead(out EyeBrow2))
            return false;

        if (!reader.TryRead(out EyeBrow3))
            return false;

        if (!reader.TryRead(out EyeBrow4))
            return false;

        if (!reader.TryRead(out MakeUp))
            return false;

        if (!reader.TryRead(out Personality))
            return false;

        if (!reader.TryRead(out PersonalityX))
            return false;

        if (!reader.TryRead(out PersonalityY))
            return false;

        return true;
    }

    public override string ToString()
    {
        return $"""
               {nameof(Hair)}: {Hair}
               {nameof(Color)}: {Color}
               {nameof(Skin)}: {Skin}
               {nameof(Eye1)}: {Eye1}
               {nameof(Eye2)}: {Eye2}
               {nameof(Eye3)}: {Eye3}
               {nameof(Nose1)}: {Nose1}
               {nameof(Nose2)}: {Nose2}
               {nameof(Nose3)}: {Nose3}
               {nameof(Nose4)}: {Nose4}
               {nameof(Mouth1)}: {Mouth1}
               {nameof(Mouth2)}: {Mouth2}
               {nameof(Mouth3)}: {Mouth3}
               {nameof(Jaws1)}: {Jaws1}
               {nameof(Jaws2)}: {Jaws2}
               {nameof(Face1)}: {Face1}
               {nameof(Face2)}: {Face2}
               {nameof(EyeBrow1)}: {EyeBrow1}
               {nameof(EyeBrow2)}: {EyeBrow2}
               {nameof(EyeBrow3)}: {EyeBrow3}
               {nameof(EyeBrow4)}: {EyeBrow4}
               {nameof(MakeUp)}: {MakeUp}
               {nameof(Personality)}: {Personality}
               {nameof(PersonalityX)}: {PersonalityX}
               {nameof(PersonalityY)}: {PersonalityY}
               """;
    }
}
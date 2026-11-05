using System;
using System.IO;
using System.Text;

namespace Lunarite.Network;

public class PacketWriter : IDisposable
{
    private readonly MemoryStream _stream;
    private readonly BinaryWriter _writer;

    public byte[] Buffer => _stream.ToArray();

    public PacketWriter()
    {
        _stream = new MemoryStream();
        _writer = new BinaryWriter(_stream);
    }

    public void Write(byte value) => _writer.Write(value);

    public void Write(bool value) => _writer.Write(value);

    public void Write(short value) => _writer.Write(value);

    public void Write(ushort value) => _writer.Write(value);

    public void Write(int value) => _writer.Write(value);

    public void Write(uint value) => _writer.Write(value);

    public void Write(long value) => _writer.Write(value);

    public void Write(ulong value) => _writer.Write(value);

    public void Write(float value) => _writer.Write(value);

    public void Write(double value) => _writer.Write(value);

    public void Write(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            _writer.Write('\0');
            return;
        }

        var bytes = Encoding.Latin1.GetBytes(value); // byte for byte, see PacketReader.TryRead(out string)

        _writer.Write(bytes);
        _writer.Write('\0');
    }

    public void Write(byte[] value) => _writer.Write(value);

    public void Write(Span<byte> value) => _writer.Write(value);

    #region Helpers

    public void Write(Enum value)
    {
        var typeCode = value.GetTypeCode();
        var enumValue = Convert.ChangeType(value, typeCode);

        switch (typeCode)
        {
            case TypeCode.Byte:
                _writer.Write((byte)enumValue);
                break;

            case TypeCode.SByte:
                _writer.Write((sbyte)enumValue);
                break;

            case TypeCode.Int16:
                _writer.Write((short)enumValue);
                break;

            case TypeCode.UInt16:
                _writer.Write((ushort)enumValue);
                break;

            case TypeCode.Int32:
                _writer.Write((int)enumValue);
                break;

            case TypeCode.UInt32:
                _writer.Write((uint)enumValue);
                break;

            case TypeCode.Int64:
                _writer.Write((long)enumValue);
                break;

            case TypeCode.UInt64:
                _writer.Write((ulong)enumValue);
                break;

            default:
                throw new NotSupportedException();
        }
    }

    #endregion

    public void Dispose()
    {
        _writer.Close();
    }
}
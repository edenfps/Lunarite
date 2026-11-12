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

        // Byte for byte (see PacketReader.TryRead(out string)): text from the client stays in its code page.
        // Text the server writes in Hangul (Korean messages, PlayerLanguage) holds chars above 0xFF, which
        // Latin-1 would turn into '?': that goes out as CP949, what a Korean client reads.
        var bytes = HasWideChars(value) ? Cp949.Value.GetBytes(value) : Encoding.Latin1.GetBytes(value);

        _writer.Write(bytes);
        _writer.Write('\0');
    }

    public void Write(byte[] value) => _writer.Write(value);

    private static bool HasWideChars(string value)
    {
        foreach (char c in value) if (c > 0xFF) return true;
        return false;
    }

    private static readonly Lazy<Encoding> Cp949 = new(() =>
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(949);
    });

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
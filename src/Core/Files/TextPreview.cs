// SPDX-License-Identifier: GPL-3.0-only
using System.Globalization;
using System.Text;

namespace Edex.Core.Files;

public static class TextPreview
{
    private const int Utf16BomLength = 2;
    private const int Utf8BomLength = 3;
    private const byte Utf16LeFirst = 0xFF;
    private const byte Utf16LeSecond = 0xFE;
    private const byte Utf16BeFirst = 0xFE;
    private const byte Utf16BeSecond = 0xFF;
    private const byte Utf8First = 0xEF;
    private const byte Utf8Second = 0xBB;
    private const byte Utf8Third = 0xBF;
    private const int BinaryScanLimit = 8192;
    private const int HexDumpLimit = 4096;
    private const int HexBytesPerLine = 16;

    public static string Decode(byte[] bytes)
    {
        if (HasBom(bytes, Utf16LeFirst, Utf16LeSecond))
        {
            return Encoding.Unicode.GetString(bytes, Utf16BomLength, bytes.Length - Utf16BomLength);
        }

        if (HasBom(bytes, Utf16BeFirst, Utf16BeSecond))
        {
            return Encoding.BigEndianUnicode.GetString(bytes, Utf16BomLength, bytes.Length - Utf16BomLength);
        }

        if (LooksBinary(bytes))
        {
            return HexDump(bytes);
        }

        int start = HasUtf8Bom(bytes) ? Utf8BomLength : 0;
        return Encoding.UTF8.GetString(bytes, start, bytes.Length - start);
    }

    private static bool HasBom(byte[] bytes, byte first, byte second) =>
        bytes.Length >= Utf16BomLength && bytes[0] == first && bytes[1] == second;

    private static bool HasUtf8Bom(byte[] bytes) =>
        bytes.Length >= Utf8BomLength && bytes[0] == Utf8First && bytes[1] == Utf8Second && bytes[2] == Utf8Third;

    private static bool LooksBinary(byte[] bytes) =>
        Array.IndexOf(bytes, (byte)0, 0, Math.Min(bytes.Length, BinaryScanLimit)) >= 0;

    private static string HexDump(byte[] bytes)
    {
        var hex = new StringBuilder();
        int limit = Math.Min(bytes.Length, HexDumpLimit);
        for (int i = 0; i < limit; i += HexBytesPerLine)
        {
            hex.Append(i.ToString("x8", CultureInfo.InvariantCulture)).Append("  ");
            for (int j = i; j < i + HexBytesPerLine && j < bytes.Length; j++)
            {
                hex.Append(bytes[j].ToString("x2", CultureInfo.InvariantCulture)).Append(' ');
            }

            hex.Append('\n');
        }
        return hex.ToString();
    }
}

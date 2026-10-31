// SPDX-License-Identifier: GPL-3.0-only
using System.Text;

namespace Edex.Core.Files;

public static class TextPreview
{
    /// <summary>UTF-8 (with or without BOM) or UTF-16 by BOM; binary-looking content is shown as a hex preview.</summary>
    public static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        int sample = Math.Min(bytes.Length, 8192);
        if (Array.IndexOf(bytes, (byte)0, 0, sample) >= 0)
        {
            var hex = new StringBuilder();
            for (int i = 0; i < Math.Min(bytes.Length, 4096); i += 16)
            {
                hex.Append(i.ToString("x8", System.Globalization.CultureInfo.InvariantCulture)).Append("  ");
                for (int j = i; j < i + 16 && j < bytes.Length; j++) hex.Append(bytes[j].ToString("x2", System.Globalization.CultureInfo.InvariantCulture)).Append(' ');
                hex.Append('\n');
            }
            return hex.ToString();
        }
        int start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
        return Encoding.UTF8.GetString(bytes, start, bytes.Length - start);
    }

}

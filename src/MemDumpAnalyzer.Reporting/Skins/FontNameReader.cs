using System.Buffers.Binary;
using System.Text;

namespace MemDumpAnalyzer.Reporting.Skins;

/// <summary>
/// Reads the PostScript name (name ID 6) from a TrueType/OpenType font's 'name' table. QuestPDF only
/// knows fonts by the names stored in the file, so this links a skin's font file to what QuestPDF registered.
/// </summary>
internal static class FontNameReader
{
    public static string? ReadPostScriptName(byte[] font)
    {
        try
        {
            var span = font.AsSpan();
            if (span.Length < 12) return null;
            int numTables = BinaryPrimitives.ReadUInt16BigEndian(span[4..]);
            for (int i = 0; i < numTables; i++)
            {
                var record = span.Slice(12 + i * 16, 16);
                if (Encoding.ASCII.GetString(record[..4]) != "name") continue;
                int tableOffset = (int)BinaryPrimitives.ReadUInt32BigEndian(record[8..]);
                return ReadNameTable(span[tableOffset..], nameId: 6);
            }
        }
        catch (ArgumentOutOfRangeException)
        {
            // Truncated or not a font: no name
        }
        return null;
    }

    private static string? ReadNameTable(ReadOnlySpan<byte> table, int nameId)
    {
        int count = BinaryPrimitives.ReadUInt16BigEndian(table[2..]);
        int stringOffset = BinaryPrimitives.ReadUInt16BigEndian(table[4..]);
        string? macName = null;
        for (int i = 0; i < count; i++)
        {
            var r = table.Slice(6 + i * 12, 12);
            int platformId = BinaryPrimitives.ReadUInt16BigEndian(r);
            int id = BinaryPrimitives.ReadUInt16BigEndian(r[6..]);
            if (id != nameId) continue;
            int length = BinaryPrimitives.ReadUInt16BigEndian(r[8..]);
            int offset = BinaryPrimitives.ReadUInt16BigEndian(r[10..]);
            var bytes = table.Slice(stringOffset + offset, length);
            if (platformId is 0 or 3) return Encoding.BigEndianUnicode.GetString(bytes);   // Unicode / Windows: UTF-16BE
            if (platformId == 1) macName ??= Encoding.ASCII.GetString(bytes);              // Macintosh: ASCII for PostScript names
        }
        return macName;
    }
}

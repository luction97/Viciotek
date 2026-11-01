using System.Text;
namespace Viciotek.Infrastructure.Detection;
/// <summary>Reads only the game classification for installed IDs, offline, from appinfo v39-v41. Unknown/corrupt formats fail closed.</summary>
internal static class SteamAppTypes
{
    public static HashSet<int> ReadGames(string file, IReadOnlySet<int> installed)
    {
        var games = new HashSet<int>();
        try
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (stream.Length > 512L * 1024 * 1024) return games;
            using var reader = new BinaryReader(stream, Encoding.UTF8);
            var magic = reader.ReadUInt32(); var version = (int)(magic & 255);
            if ((magic >> 8) != 0x075644 || version is < 39 or > 41) return games;
            _ = reader.ReadUInt32();
            Dictionary<int, string>? keys = null;
            if (version == 41)
            {
                var offset = reader.ReadInt64(); var start = stream.Position;
                if (offset < start || offset >= stream.Length) return games;
                stream.Position = offset; var count = reader.ReadUInt32(); if (count > 1000000) return games;
                keys = [];
                for (var i = 0; i < count; i++) { var key = Text(reader, stream.Length); if (key is "appinfo" or "common" or "type") keys[i] = key; if (keys.Count == 3) break; }
                stream.Position = start;
            }
            while (stream.Position + 4 <= stream.Length)
            {
                var id = reader.ReadInt32(); if (id == 0) break;
                var size = reader.ReadUInt32(); var end = stream.Position + size;
                if (size < 40 || end > stream.Length) throw new InvalidDataException();
                if (installed.Contains(id))
                {
                    stream.Position += version >= 40 ? 60 : 40;
                    int count = 0; string? type = null;
                    Walk(reader, end, keys, 0, "root", ref count, ref type);
                    if (stream.Position == end && count == 1 && type == "game") games.Add(id);
                }
                stream.Position = end;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or OverflowException) { return []; }
        return games;
    }
    private static string Text(BinaryReader reader, long end)
    {
        var bytes = new List<byte>();
        while (reader.BaseStream.Position < end && bytes.Count < 4096) { var b = reader.ReadByte(); if (b == 0) return Encoding.UTF8.GetString(bytes.ToArray()); bytes.Add(b); }
        throw new InvalidDataException();
    }
    private static void Walk(BinaryReader reader, long end, Dictionary<int, string>? keys, int depth, string scope, ref int count, ref string? gameType)
    {
        if (depth > 32) throw new InvalidDataException();
        while (reader.BaseStream.Position < end)
        {
            var type = reader.ReadByte(); if (type is 8 or 11) return;
            var key = keys is null ? Text(reader, end) : keys.GetValueOrDefault(reader.ReadInt32(), "?");
            if (type == 0) { var child = scope == "root" && key == "appinfo" ? "app" : (scope is "root" or "app") && key == "common" ? "common" : "other"; Walk(reader, end, keys, depth + 1, child, ref count, ref gameType); }
            else if (type == 1) { var value = Text(reader, end); if (scope == "common" && key == "type") { count++; gameType = value.ToLowerInvariant(); } }
            else if (type is 2 or 3 or 4 or 6) reader.BaseStream.Position += 4;
            else if (type is 7 or 10) reader.BaseStream.Position += 8;
            else if (type == 5) { var chars = 0; while (reader.BaseStream.Position + 2 <= end && reader.ReadUInt16() != 0) if (++chars > 4096) throw new InvalidDataException(); }
            else throw new InvalidDataException();
            if (reader.BaseStream.Position > end) throw new InvalidDataException();
        }
        throw new InvalidDataException();
    }
}

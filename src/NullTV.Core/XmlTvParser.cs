using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace NullTV.Core;

public static class XmlTvParser
{
    public static async Task<List<Programme>> ParseAsync(Stream stream, DateTimeOffset from, DateTimeOffset until, CancellationToken ct = default)
    {
        var result = new List<Programme>();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { Async = true, DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreWhitespace = true, MaxCharactersInDocument = 512L * 1024 * 1024 });
        while (await reader.ReadAsync())
        {
            ct.ThrowIfCancellationRequested();
            if (reader.NodeType != XmlNodeType.Element || reader.Name != "programme") continue;
            using var sub = reader.ReadSubtree();
            var e = await XElement.LoadAsync(sub, LoadOptions.None, ct);
            if (!TryTime((string?)e.Attribute("start"), out var start) || !TryTime((string?)e.Attribute("stop"), out var end) || end <= start || end < from || start > until) continue;
            result.Add(new((string?)e.Attribute("channel") ?? "", (string?)e.Element("title") ?? "Untitled", (string?)e.Element("desc") ?? "", start, end));
        }
        return result.OrderBy(p => p.Start).ToList();
    }
    public static bool TryTime(string? value, out DateTimeOffset result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var parts = value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (!DateTime.TryParseExact(parts[0], "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return false;
        var offset = TimeSpan.Zero;
        if (parts.Length > 1)
        {
            var z = parts[1]; if (z.Length != 5 || z[0] is not ('+' or '-') || !int.TryParse(z[1..3], out var hours) || !int.TryParse(z[3..5], out var minutes) || minutes > 59 || hours > 14 || (hours == 14 && minutes != 0)) return false;
            offset = new TimeSpan(hours, minutes, 0) * (z[0] == '-' ? -1 : 1);
        }
        result = new(date, offset); return true;
    }
}

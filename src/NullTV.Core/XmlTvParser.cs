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
            result.Add(new((string?)e.Attribute("channel") ?? "", (string?)e.Element("title") ?? "Untitled", (string?)e.Element("desc") ?? "", start, end) {
                Subtitle=(string?)e.Element("sub-title")??"", Category=string.Join(" · ",e.Elements("category").Select(c=>c.Value).Distinct()),
                Rating=string.Join(" ",new[]{(string?)e.Element("rating")?.Attribute("system"),(string?)e.Element("rating")?.Element("value")}.Where(v=>!string.IsNullOrWhiteSpace(v))),
                Episode=EpisodeLabel(e), IsNew=e.Element("new")!=null, IsPremiere=e.Element("premiere")!=null, IsRepeat=e.Element("previously-shown")!=null
            });
        }
        return result.OrderBy(p => p.Start).ToList();
    }
    private static string EpisodeLabel(XElement programme)
    {
        var episodes=programme.Elements("episode-num").ToArray();
        var screen=episodes.FirstOrDefault(e=>(string?)e.Attribute("system") is null or "onscreen"); if(screen!=null) return screen.Value;
        var indexed=episodes.FirstOrDefault(e=>(string?)e.Attribute("system")=="xmltv_ns"); if(indexed==null) return "";
        var parts=indexed.Value.Split('.'); var labels=new List<string>();
        for(int n=0;n<Math.Min(2,parts.Length);n++) if(int.TryParse(parts[n].Split('/')[0].Trim(),out var value) && value is >=0 and <100000) labels.Add((n==0 ? "S" : "E")+(value+1));
        return string.Join(" · ",labels);
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

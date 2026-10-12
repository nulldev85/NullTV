using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace NullTV.Core;

public sealed partial class M3uParser
{
    [GeneratedRegex("([\\w-]+)\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)'|([^\\s,]+))")]
    private static partial Regex Attributes();
    public string EpgUrl { get; private set; } = "";
    public int Skipped { get; private set; }
    public async IAsyncEnumerable<MediaItem> ParseAsync(TextReader reader, string sourceId, [EnumeratorCancellation] CancellationToken ct = default)
    {
        Dictionary<string,string>? info = null;
        var headers = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        string name = "";
        Dictionary<string,string> defaults = [];
        int count = 0;
        while (await reader.ReadLineAsync(ct) is { } raw)
        {
            ct.ThrowIfCancellationRequested();
            var line = raw.Trim().TrimStart('\uFEFF');
            if (line.Length > 64 * 1024) { Skipped++; info = null; continue; }
            if (line.StartsWith("#EXTM3U", StringComparison.OrdinalIgnoreCase))
            {
                var a = ReadAttributes(line);
                EpgUrl = a.GetValueOrDefault("x-tvg-url", a.GetValueOrDefault("url-tvg", "")).Split(',')[0];
                defaults = a.Where(x=>x.Key.StartsWith("catchup",StringComparison.OrdinalIgnoreCase)).ToDictionary(x=>x.Key,x=>x.Value);
            }
            else if (line.StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase))
            {
                info = ReadAttributes(line);
                foreach(var entry in defaults) info.TryAdd(entry.Key,entry.Value);
                var comma = FindComma(line);
                name = comma >= 0 ? line[(comma + 1)..].Trim() : "";
                if(name=="") name=info.GetValueOrDefault("tvg-name","Unnamed channel");
                headers = new(StringComparer.OrdinalIgnoreCase);
            }
            else if (line.StartsWith("#EXTVLCOPT:", StringComparison.OrdinalIgnoreCase) || line.StartsWith("#KODIPROP:", StringComparison.OrdinalIgnoreCase))
            {
                var opt = line[(line.IndexOf(':') + 1)..]; var eq = opt.IndexOf('=');
                if (eq > 0)
                {
                    var key = opt[..eq].ToLowerInvariant(); var value = opt[(eq + 1)..].Trim();
                    if (key is "http-user-agent" or "user-agent") headers["User-Agent"] = Clean(value);
                    if (key is "http-referrer" or "http-referer" or "referrer") headers["Referer"] = Clean(value);
                    if (key.EndsWith("stream_headers") || key.EndsWith("manifest_headers"))
                        foreach (var part in value.Split('&')) { var p = part.IndexOf('='); if (p > 0) { var k = part[..p]; if (k.Equals("User-Agent", StringComparison.OrdinalIgnoreCase) || k.Equals("Referer", StringComparison.OrdinalIgnoreCase)) headers[k] = Clean(Uri.UnescapeDataString(part[(p+1)..])); } }
                }
            }
            else if (line.Length > 0 && !line.StartsWith('#'))
            {
                var split = line.Split('|', 2); var url = split[0];
                if (info is null || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https" or "rtsp" or "rtmp" or "rtmps")) { Skipped++; info = null; continue; }
                if (split.Length == 2) foreach (var part in split[1].Split('&')) { var p = part.IndexOf('='); if (p > 0) { var k = part[..p]; if (k.Equals("User-Agent", StringComparison.OrdinalIgnoreCase) || k.Equals("Referer", StringComparison.OrdinalIgnoreCase)) headers[k] = Clean(Uri.UnescapeDataString(part[(p+1)..])); } }
                int.TryParse(info.GetValueOrDefault("catchup-days", "0"), out var days);
                var id = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(sourceId + url)))[..24];
                yield return new(id, sourceId, string.IsNullOrWhiteSpace(name) ? "Unnamed channel" : name, info.GetValueOrDefault("group-title", "Unsorted"), MediaKind.Live, url)
                {
                    Number = ++count, EpgId = info.GetValueOrDefault("tvg-id", ""), Logo = SafeImage(info.GetValueOrDefault("tvg-logo", "")),
                    ArchiveDays = Math.Clamp(days, 0, 365), Catchup = info.GetValueOrDefault("catchup", ""), CatchupTemplate = info.GetValueOrDefault("catchup-source", ""), Headers = new(headers)
                };
                info = null;
            }
        }
    }
    private static string Clean(string s) => s.Replace("\r", "").Replace("\n", "");
    public static string SafeImage(string s) => Uri.TryCreate(s, UriKind.Absolute, out var u) && u.Scheme is "https" or "http" ? s : "";
    private static Dictionary<string,string> ReadAttributes(string s) => Attributes().Matches(s).Cast<Match>().GroupBy(m => m.Groups[1].Value, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => { var m = g.Last(); return m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Success ? m.Groups[3].Value : m.Groups[4].Value; }, StringComparer.OrdinalIgnoreCase);
    private static int FindComma(string s) { char quote = '\0'; for (int i = 0; i < s.Length; i++) { if (quote == '\0' && s[i] is '\'' or '"') quote = s[i]; else if (s[i] == quote) quote = '\0'; else if (quote == '\0' && s[i] == ',') return i; } return -1; }
}

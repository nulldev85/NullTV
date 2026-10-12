using System.Globalization;
using System.Text;
using System.Text.Json;

namespace NullTV.Core;

public sealed class XtreamClient(HttpClient http)
{
    public static Uri BaseUri(Source s)
    {
        if (!Uri.TryCreate(s.Address.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var u) || u.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(u.UserInfo)) throw new InvalidDataException("Enter an HTTP or HTTPS server URL without embedded credentials.");
        return u;
    }
    public static Uri ApiUri(Source s, string? action = null, string? id = null, string idKey = "stream_id")
    {
        var query = $"username={Uri.EscapeDataString(s.Username)}&password={Uri.EscapeDataString(s.Password)}";
        if (action != null) query += $"&action={Uri.EscapeDataString(action)}";
        if (id != null) query += $"&{idKey}={Uri.EscapeDataString(id)}";
        return new(BaseUri(s), "player_api.php?" + query);
    }
    public async Task<JsonDocument> RequestAsync(Source s, string? action = null, string? id = null, string idKey = "stream_id", CancellationToken ct = default)
    {
        using var response = await http.GetAsync(ApiUri(s, action, id, idKey), HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden) throw new InvalidDataException("The provider rejected these credentials.");
        if (!response.IsSuccessStatusCode) throw new InvalidDataException($"The provider returned HTTP {(int)response.StatusCode}.");
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        try { return await JsonDocument.ParseAsync(stream, cancellationToken: ct); } catch (JsonException) { throw new InvalidDataException("The provider returned malformed JSON."); }
    }
    public async Task<Catalog> LoadAsync(Source s, CancellationToken ct = default)
    {
        using var account = await RequestAsync(s, ct: ct);
        var root = account.RootElement;
        if (!root.TryGetProperty("user_info", out var user) || Text(user, "auth") != "1") throw new InvalidDataException("Login failed. Check the server URL, username, and password.");
        var status = Text(user, "status");
        DateTimeOffset? expires = long.TryParse(Text(user, "exp_date"), out var epoch) && epoch is > 0 and < 253402300799 ? DateTimeOffset.FromUnixTimeSeconds(epoch) : null;
        s.AccountStatus = status; s.Expires = expires;
        if (!status.Equals("Active", StringComparison.OrdinalIgnoreCase) || expires < DateTimeOffset.UtcNow) throw new InvalidDataException($"Account is {status}; expiry: {expires?.ToLocalTime().ToString("g") ?? "not supplied"}.");
        var items = new List<MediaItem>();
        foreach (var (prefix, kind, action) in new[] { ("live", MediaKind.Live, "get_live_streams"), ("vod", MediaKind.Movie, "get_vod_streams"), ("series", MediaKind.Series, "get_series") })
        {
            using var cats = await RequestAsync(s, $"get_{prefix}_categories", ct: ct);
            var categories = cats.RootElement.ValueKind == JsonValueKind.Array ? cats.RootElement.EnumerateArray().Where(e => Text(e,"category_id") != "").GroupBy(e => Text(e,"category_id")).ToDictionary(g => g.Key, g => Text(g.First(),"category_name")) : [];
            using var streams = await RequestAsync(s, action, ct: ct);
            if (streams.RootElement.ValueKind != JsonValueKind.Array) continue;
            foreach (var e in streams.RootElement.EnumerateArray())
            {
                var id = Text(e, kind == MediaKind.Series ? "series_id" : "stream_id"); if (id == "") continue;
                var ext = kind == MediaKind.Live ? s.LiveExtension : SafeExtension(Text(e, "container_extension"));
                items.Add(new($"{s.Id}:{kind}:{id}", s.Id, Text(e,"name"), categories.GetValueOrDefault(Text(e,"category_id"),"Unsorted"), kind, kind == MediaKind.Series ? "" : StreamUrl(s, kind, id, ext))
                {
                    ProviderId = id, Number = items.Count + 1, EpgId = Text(e,"epg_channel_id"), Logo = M3uParser.SafeImage(Text(e, kind == MediaKind.Series ? "cover" : "stream_icon")), Extension = ext,
                    ArchiveDays = Text(e,"tv_archive") == "1" && int.TryParse(Text(e,"tv_archive_duration"), out var days) ? Math.Clamp(days,0,365) : 0, Catchup = Text(e,"tv_archive") == "1" ? "xtream" : ""
                });
            }
        }
        var epg = new Uri(BaseUri(s), $"xmltv.php?username={Uri.EscapeDataString(s.Username)}&password={Uri.EscapeDataString(s.Password)}").AbsoluteUri;
        return new(items, epg, status, expires);
    }
    public static string StreamUrl(Source s, MediaKind kind, string id, string ext) => new Uri(BaseUri(s), $"{(kind == MediaKind.Live ? "live" : kind == MediaKind.Movie ? "movie" : "series")}/{Uri.EscapeDataString(s.Username)}/{Uri.EscapeDataString(s.Password)}/{Uri.EscapeDataString(id)}.{SafeExtension(ext)}").AbsoluteUri;
    private static string SafeExtension(string ext) => ext.Length is > 0 and < 10 && ext.All(char.IsAsciiLetterOrDigit) ? ext : "mp4";
    public async Task<MediaDetails> DetailsAsync(Source s, MediaItem item, CancellationToken ct = default)
    {
        using var doc = await RequestAsync(s, item.Kind == MediaKind.Series ? "get_series_info" : "get_vod_info", item.ProviderId, item.Kind == MediaKind.Series ? "series_id" : "vod_id", ct);
        var root = doc.RootElement; var info = root.TryGetProperty("info", out var i) ? i : root;
        var episodes = new List<MediaItem>();
        if (root.TryGetProperty("episodes", out var seasons) && seasons.ValueKind == JsonValueKind.Object)
            foreach (var season in seasons.EnumerateObject().OrderBy(s => int.TryParse(s.Name, out var n) ? n : 0))
                if (season.Value.ValueKind == JsonValueKind.Array) foreach (var e in season.Value.EnumerateArray())
                {
                    var id = Text(e,"id"); if (id == "") continue;
                    var ext = SafeExtension(Text(e,"container_extension"));
                    episodes.Add(new($"{s.Id}:Episode:{id}", s.Id, $"S{season.Name} · E{Text(e,"episode_num")} · {Text(e,"title")}", item.Name, MediaKind.Episode, StreamUrl(s, MediaKind.Episode, id, ext)) { ProviderId=id, Extension=ext });
                }
        return new(Text(info,"plot"), Text(info,"cast"), Text(info,"rating"), episodes);
    }
    public async Task<List<Programme>> EpgAsync(Source s, MediaItem item, bool full, CancellationToken ct = default)
    {
        using var doc = await RequestAsync(s, full ? "get_simple_data_table" : "get_short_epg", item.ProviderId, ct: ct);
        var result = new List<Programme>();
        if (doc.RootElement.TryGetProperty("epg_listings", out var listings) && listings.ValueKind == JsonValueKind.Array)
            foreach (var e in listings.EnumerateArray())
            {
                if (!long.TryParse(Text(e,"start_timestamp"), out var start) || !long.TryParse(Text(e,"stop_timestamp"), out var end) || start is < 0 or > 253402300799 || end is < 0 or > 253402300799 || end <= start) continue;
                result.Add(new(item.EpgId == "" ? item.Id : item.EpgId, Decode(Text(e,"title")), Decode(Text(e,"description")), DateTimeOffset.FromUnixTimeSeconds(start), DateTimeOffset.FromUnixTimeSeconds(end)));
            }
        return result;
    }
    public static string Text(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind != JsonValueKind.Null ? v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ToString() : "";
    private static string Decode(string s) { try { return Encoding.UTF8.GetString(Convert.FromBase64String(s)); } catch (FormatException) { return s; } }
    public static string CatchupUrl(Source s, MediaItem item, Programme p)
    {
        if (item.ArchiveDays <= 0 || p.Start > DateTimeOffset.UtcNow || p.Start < DateTimeOffset.UtcNow.AddDays(-item.ArchiveDays)) throw new InvalidDataException("This programme is outside the provider's archive window.");
        if (item.Catchup == "xtream")
            return new Uri(BaseUri(s), $"timeshift/{Uri.EscapeDataString(s.Username)}/{Uri.EscapeDataString(s.Password)}/{Math.Max(1,(int)Math.Ceiling((p.End-p.Start).TotalMinutes))}/{p.Start.UtcDateTime:yyyy-MM-dd:HH-mm}/{Uri.EscapeDataString(item.ProviderId)}.ts").AbsoluteUri;
        if (item.CatchupTemplate != "")
        {
            var url = item.CatchupTemplate.Replace("{utc}", p.Start.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)).Replace("{utcend}", p.End.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)).Replace("{duration}", ((long)(p.End-p.Start).TotalSeconds).ToString(CultureInfo.InvariantCulture));
            if (Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme is "http" or "https") return url;
        }
        throw new InvalidDataException("The provider did not supply a supported catch-up template.");
    }
}

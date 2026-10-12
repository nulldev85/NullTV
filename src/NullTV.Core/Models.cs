using System.Text.Json.Serialization;

namespace NullTV.Core;

public enum SourceKind { Xtream, PlaylistUrl, PlaylistFile, Jellyfin, Emby, Plex }
public enum MediaKind { Live, Movie, Series, Episode }
public sealed class Source
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "My source";
    public SourceKind Kind { get; set; }
    public string Address { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string UserId { get; set; } = "";
    public string EpgOverride { get; set; } = "";
    public string LiveExtension { get; set; } = "ts";
    public int RefreshHours { get; set; } = 12;
    public DateTimeOffset LastRefresh { get; set; }
    public string AccountStatus { get; set; } = "Not tested";
    public DateTimeOffset? Expires { get; set; }
    public Dictionary<string, string> EpgMappings { get; set; } = [];
}
public sealed record MediaItem(string Id, string SourceId, string Name, string Category, MediaKind Kind, string Url)
{
    public string Logo { get; init; } = "";
    public string EpgId { get; init; } = "";
    public int Number { get; init; }
    public string ProviderId { get; init; } = "";
    public string Extension { get; init; } = "mp4";
    public int ArchiveDays { get; init; }
    public string Catchup { get; init; } = "";
    public string CatchupTemplate { get; init; } = "";
    public Dictionary<string, string> Headers { get; init; } = [];
    public override string ToString() => Name;
}
public sealed record Programme(string ChannelId, string Title, string Description, DateTimeOffset Start, DateTimeOffset End)
{
    public string Subtitle { get; init; } = "";
    public string Category { get; init; } = "";
    public string Rating { get; init; } = "";
    public string Episode { get; init; } = "";
    public bool IsNew { get; init; }
    public bool IsPremiere { get; init; }
    public bool IsRepeat { get; init; }
    public string TimeLabel => $"{Start.LocalDateTime:HH:mm} – {End.LocalDateTime:HH:mm}";
    public override string ToString() => $"{TimeLabel}  {Title}";
}
public sealed record MediaDetails(string Plot, string Cast, string Rating, List<MediaItem> Episodes);
public sealed record Catalog(List<MediaItem> Items, string EpgUrl, string Status, DateTimeOffset? Expires)
{
    public Dictionary<string,WatchState> RemoteHistory { get; init; } = [];
}
public sealed class Profile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Home";
    public List<Source> Sources { get; set; } = [];
    public HashSet<string> Favorites { get; set; } = [];
    public List<string> FavoriteGuideOrder { get; set; } = [];
    public Dictionary<string, WatchState> History { get; set; } = [];
    public HashSet<string> LockedItems { get; set; } = [];
    public HashSet<string> LockedCategories { get; set; } = [];
    public List<Reminder> Reminders { get; set; } = [];
    public string PinHash { get; set; } = "";
    public string PinSalt { get; set; } = "";
}
public sealed record WatchState(double Position, double Duration, bool Watched, DateTimeOffset Updated);
public sealed record Reminder(string ChannelId, string Title, DateTimeOffset Start);
public sealed class Settings
{
    public int Schema { get; set; } = 1;
    public string CurrentProfileId { get; set; } = "";
    public List<Profile> Profiles { get; set; } = [new()];
    public PlaybackSettings Playback { get; set; } = new();
    public AppearanceSettings Appearance { get; set; } = new();
    public string StartupTab { get; set; } = "Live TV";
    public bool ResumeOnLaunch { get; set; }
    public int LockTimeoutMinutes { get; set; } = 10;
    public Dictionary<string, string> Keymap { get; set; } = new() { ["Space"]="Pause", ["F"]="Fullscreen", ["Up"]="Previous", ["Down"]="Next", ["M"]="Mute" };
    [JsonIgnore] public Profile Profile => Profiles.FirstOrDefault(p => p.Id == CurrentProfileId) ?? Profiles[0];
    public void Validate()
    {
        if (Schema != 1) throw new InvalidDataException("This settings version is newer than this app.");
        if (Profiles is null || Playback is null || Appearance is null || Keymap is null || Profiles.Any(p=>p is null || p.Sources is null || p.Favorites is null || p.FavoriteGuideOrder is null || p.History is null || p.LockedItems is null || p.LockedCategories is null || p.Reminders is null)) throw new InvalidDataException("Settings contain invalid or missing fields.");
        if (Profiles.Count == 0) Profiles.Add(new());
        Playback.Volume = Math.Clamp(Playback.Volume, 0, 100);
        Playback.CacheSeconds = Math.Clamp(Playback.CacheSeconds, 1, 120);
        Playback.BufferMiB = Math.Clamp(Playback.BufferMiB, 8, 512);
        Appearance.FontScale = Math.Clamp(Appearance.FontScale, .8, 1.5);
        if (Appearance.Theme is "OLED" or "Dark" or "Dracula OLED")
        {
            Appearance.Theme = "Dracula";
            Appearance.Accent = "#BD93F9";
        }
        LockTimeoutMinutes = Math.Clamp(LockTimeoutMinutes, 1, 120);
        foreach (var s in Profiles.SelectMany(p => p.Sources)) { if(s is null || s.Id is null || s.Id.Length!=32 || !s.Id.All(char.IsAsciiHexDigit) || s.EpgMappings is null || s.Address is null || s.Password is null || s.Username is null || s.ApiKey is null) throw new InvalidDataException("Settings contain an invalid source."); s.RefreshHours = Math.Clamp(s.RefreshHours, 1, 168); if(s.LiveExtension is not ("ts" or "m3u8")) s.LiveExtension="ts"; }
        if(Profiles.SelectMany(p=>p.Sources).GroupBy(s=>s.Id).Any(g=>g.Count()>1)) throw new InvalidDataException("Source identifiers must be unique.");
    }
}
public sealed class PlaybackSettings
{
    public bool HardwareDecode { get; set; } = true;
    public bool LowLatency { get; set; }
    public int CacheSeconds { get; set; } = 8;
    public int BufferMiB { get; set; } = 64;
    public string AudioLanguage { get; set; } = "eng";
    public string SubtitleLanguage { get; set; } = "eng";
    public bool SubtitlesEnabled { get; set; }
    public string AudioDevice { get; set; } = "auto";
    public bool Passthrough { get; set; }
    public bool Deinterlace { get; set; }
    public string Aspect { get; set; } = "Default";
    public int Volume { get; set; } = 75;
    public bool Hdr { get; set; } = true;
    public string SubtitleFont { get; set; } = "Segoe UI";
    public int SubtitleSize { get; set; } = 42;
    public string SubtitleColor { get; set; } = "#FFFFFFFF";
    public string SubtitleBackground { get; set; } = "#80000000";
    public int SubtitlePosition { get; set; } = 95;
}
public sealed class AppearanceSettings
{
    public string Theme { get; set; } = "Dracula";
    public string Accent { get; set; } = "#BD93F9";
    public double FontScale { get; set; } = 1;
    public bool Compact { get; set; }
    public bool ReducedMotion { get; set; }
    public bool Mica { get; set; } = true;
}

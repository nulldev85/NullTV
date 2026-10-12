# Feature status

All five navigation views perform real operations. This is an MVP, not the entire requested production feature set.

| Area | Implemented | Partial / deferred |
|---|---|---|
| Build | Native ARM64 WinUI 3, per-user ARM64 MSI with Start menu/uninstall registration, self-contained portable folder + ZIP, ARM64 PE checks, build.ps1, Actions workflow | MSIX unsigned; code signing and Store certification deferred |
| Sources | Xtream account/status/expiry, all requested catalog/info/EPG endpoints, M3U local/URL, 50k parser, VLC/Kodi headers, multiple profiles/sources, reorder/test/edit/delete, refresh intervals | TS and HLS (.m3u8) live output selectable; catch-up timestamp format varies by provider |
| Live | Categories, search, logos, visible favorite stars, history, numbers + Enter, channel step, pause/stop/volume/seek, audio/subtitle selection, aspect, stats, reconnect | Navigation and channel panes collapse, with window expansion separate from fullscreen; native child XAML hover controls and upper-right stream panel above the video; numeric IDs assigned globally across the profile |
| Multiview / PiP | Four independently selected player surfaces, separate CompactOverlay window | Additional streams require provider connection allowance; PiP uses another stream session |
| Guide | Favorite channels with per-profile saved drag order, logos/numbers, two-hour proportional grid, in-Guide native preview from channel-name clicks with fullscreen/Escape restoration, per-channel progress/remaining time, channel/programme search, synopsis/time/duration/genre/rating/episode details, reminders, verified catch-up request | Provider metadata may be absent; in-app reminders only; no stale-data banner yet |
| Movies / Series | Posters, categories, search, plot/cast/rating, seasons/episode labels, resume, continue watching, watched toggles, Now Playing within the library with Back to library and saved filters | No external metadata service; episode metadata limited to provider data |
| Media servers | Aligned connector cards with bundled official logos, Jellyfin/Emby login or API key + user ID, library browse, Plex PIN login, episode browse, direct play, watched sync | Plex URL entered manually; no server discovery or transcoding negotiation; Jellyfin/Emby resume import; Plex resume import limited |
| Playback settings | Hardware decode, buffer/cache, latency, languages, output identifier, passthrough, deinterlace, aspect, volume, launch resume | Output device identifier entered manually; settings recreate players |
| Subtitles | Visible CC selector with Off/Automatic/embedded tracks/external SRT/ASS/SSA/VTT/SUB files; persisted enable preference, font, size, ARGB colors/background, position | Fonts entered by name; invalid option values reported by engine |
| Appearance | Verified Dracula Classic throughout the app, light theme, Mica for light theme, accent preference, consistent native fields/dialogs/menus | Cyan chrome omitted by request; font scale affects page text; compact density and reduced-motion preferences currently stored only |
| Display / input | Fullscreen, HDR hint, keys, touch/pen taps, native vertical swipe, basic Xbox buttons | Pinch resizing, controller remapping, frame-rate switching deferred |
| Parental | PIN hash, category/item locks, unlock timeout, PIN required to replace/remove locks | Local-device parental control only; not a tamper-proof security boundary |
| General | Startup tab, English UI, release page link | Windows autostart, localization and automatic updater deferred |
| Data | DPAPI persistence/cache, encrypted portable backups, cache clear, sanitized diagnostics ZIP, rotating safe logs | Encrypted guide cache persists offline; favorites/history tied to stable stream IDs |

Validation results and remaining device risks are described in CHANGELOG.md. No provider compatibility, OLED/HDR quality, 120 Hz latency or power targets are claimed without a real source/device test.

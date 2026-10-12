# Feature status

All five navigation views perform real operations. This is an MVP, not the entire requested production feature set.

| Area | Implemented | Partial / deferred |
|---|---|---|
| Build | Native ARM64 WinUI 3, self-contained portable folder + ZIP, ARM64 PE checks, build.ps1, Actions workflow | MSIX unsigned; code signing and Store certification deferred |
| Sources | Xtream account/status/expiry, all requested catalog/info/EPG endpoints, M3U local/URL, 50k parser, VLC/Kodi headers, multiple profiles/sources, reorder/test/edit/delete, refresh intervals | TS and HLS (.m3u8) live output selectable; catch-up timestamp format varies by provider |
| Live | Categories, search, logos, favorites, history, numbers + Enter, channel step, pause/stop/volume/seek, audio/subtitle selection, aspect, stats, reconnect | Native video has no composited overlay; numeric IDs assigned globally across the profile |
| Multiview / PiP | Four independently selected player surfaces, separate CompactOverlay window | Additional streams require provider connection allowance; PiP uses another stream session |
| Guide | Two-hour proportional programme grid, time scrolling, programme title search/details, reminders, verified catch-up request | First 1,000 channels per guide viewport; in-app reminders only; no stale-data banner yet |
| Movies / Series | Posters, categories, search, plot/cast/rating, seasons/episode labels, resume, continue watching, watched toggles | No external metadata service; episode metadata limited to provider data |
| Media servers | Jellyfin/Emby login or API key + user ID, library browse, Plex PIN login, episode browse, direct play, watched sync | Plex URL entered manually; no server discovery or transcoding negotiation; Jellyfin/Emby resume import; Plex resume import limited |
| Playback settings | Hardware decode, buffer/cache, latency, languages, output identifier, passthrough, deinterlace, aspect, volume, launch resume | Output device identifier entered manually; settings recreate players |
| Subtitles | Font, size, ARGB colors/background, position | Fonts entered by name; invalid option values reported by engine |
| Appearance | Light/dark/true-black OLED, Mica, accent preference | Font scale affects page text; compact density and reduced-motion preferences currently stored only |
| Display / input | Fullscreen, HDR hint, keys, touch/pen taps, native vertical swipe, basic Xbox buttons | Pinch resizing, controller remapping, frame-rate switching deferred |
| Parental | PIN hash, category/item locks, unlock timeout, PIN required to replace/remove locks | Local-device parental control only; not a tamper-proof security boundary |
| General | Startup tab, English UI, release page link | Windows autostart, localization and automatic updater deferred |
| Data | DPAPI persistence/cache, encrypted portable backups, cache clear, sanitized diagnostics ZIP, rotating safe logs | Guide is not persisted offline; favorites/history tied to stable stream IDs |

Validation results and remaining device risks are described in CHANGELOG.md. No provider compatibility, OLED/HDR quality, 120 Hz latency or power targets are claimed without a real source/device test.

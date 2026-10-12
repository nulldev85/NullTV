# Changelog

## 0.1.0 — 2026-10-11

### Milestone 1 — Sources and Live TV

- Created native ARM64 WinUI 3 app and thin libmpv C ABI wrapper with D3D11 playback surfaces.
- Implemented playlist and Xtream source import, DPAPI persistence, cached catalogs and Live TV controls.
- Verified upstream ARM64 libmpv asset checksum and fixed URL percent-encoding, XMLTV timezone validation, and malformed-input recovery.
- Core tests: 19/19 passing; synthetic 50,000-channel playlist parsed in 328 ms on the development ARM64 machine.
- Native build: zero warnings/errors. Local H.264 validation clip rendered at 1280×720 / 30 fps with D3D11VA and zero dropped frames.
- GitHub Actions run 38199005971 succeeded on windows-11-arm in 2m 37s and uploaded the ZIP + MSIX artifact.

### Milestone 2 — Guide, libraries and settings

- Added XMLTV programme grid, details, reminders and provider-checked catch-up.
- Added VOD/series metadata, episodes, local resume and watched state.
- Added Jellyfin/Emby and Plex PIN connector UI, direct-play URLs and watched-state sync.
- Added multiview, PiP, encrypted backups, sanitized diagnostics, per-profile parental restrictions and settings forms.
- Provider and media-server integration awaits user-supplied sources; fixtures are synthetic.
- Expanded unit suite: 24/24 passing; 50,000-entry parse in 295 ms.
- Confirmed local M3U → XMLTV now/next → HTTP playback against an isolated Kestrel fixture server. No generated content or test playlist is included in app artifacts.
- Replaced native Expander controls after a WinUI rendering crash; custom collapsible settings sections render correctly.
- Corrected Surface DPI sizing, global channel numbers, M3U catch-up inheritance, credential URL escaping, empty category selectors, media-server resume import and progress sync, and multiview tile/audio selection.
- Added startup-view smoke validation with isolated data directories and guarded UI error reporting.

### Milestone 3 — Distribution

- Redesigned all five views with a vertical navigation rail, consistent typography, quiet cards and intentional empty states.
- Adopted an OLED palette of true black, warm charcoal, ivory and champagne; removed the original cyan accent.
- Kept inactive native video surfaces hidden during navigation, resize and cancelled dialogs; stopping playback restores the empty state.
- Applied the app accent to dialog hover/focus states and corrected Settings card sizing after visual checks.
- Disabled mpv's retained EOF frame so live end events reach reconnect handling; a 42-second local HTTP test confirmed the 30-second clip loaded twice automatically.
- Final validation: 24 core tests passed, five native ARM64 startup views passed, and H.264 1280×720 / 30 fps used D3D11VA with zero dropped frames. Captured native Live TV, Guide, Settings and Media Servers screenshots.
- Replaced the browser bootstrap archive with an ordinary Git source tree and updated official Actions versions.
- Added deterministic ARM64 publish script, dependency checksum lock, PE validation and GitHub Actions workflow.
- Added optional MSIX packaging and documented unsigned-package installation limits.
- Added README, feature matrix, license notices and bug-report template.

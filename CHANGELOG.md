# Changelog

## Installer 0.2.1 — 2026-10-12

- Fixed the Windows 11 prerequisite check: use a 64-bit registry search for CurrentBuildNumber instead of MSI's compatibility-dependent WindowsBuild value, which can incorrectly report 9600.
- Added an installer regression check using a safe Windows Installer session. It verifies the ARM64 template, AppSearch ordering in both installation sequences, the real registry lookup and eight prerequisite cases, including Windows 10 rejection and maintenance/uninstall access.
- Replaced the default red setup artwork with neutral NullTV branding. The native setup controls retain their readable Windows system appearance.
- Verified that the normal MSI wizard reads build 26200, passes LaunchConditions and reaches WelcomeEulaDlg. Installation into the real user profile was not performed.
- This packaging revision contains the unchanged NullTV app 0.2.0.

## 0.2.0 — 2026-10-12

- Replaced the brown accent with a neutral OLED palette and shared native styles for buttons, text, password and search fields, dropdowns and dialogs.
- Fixed published XAML resources: compiled App/DesignResources XBF and the app PRI index now ship in every package, with build-time checks for omissions.
- Added visible channel and player favorite stars; live Stop and VOD Pause/Play now follow observed engine state.
- Centered a compact transport strip and added independent navigation/channel-pane toggles plus video expansion within the normal window.
- Removed the mpv event-wait lock from the UI command path, queued commands asynchronously, observed/cached playback properties and initialized players only when needed.
- Indexed programme schedules, reused channel rows, debounced search and virtualized guide cells. Guide data now persists encrypted across restarts.
- Reworked artwork loading around stable bound rows, shared downloads, visible-image retention and transient retry.
- Added a per-user ARM64 MSI with Start menu shortcut, upgrade support and Windows uninstall registration.
- Regression suite: 28/28 passing; 50,000 entries parsed in 350 ms, 390,000-programme index built in 116 ms and 50,000 now/next lookups in 22 ms on the development ARM64 machine.
- Native validation uses an isolated 50,000-channel / 390,000-programme / 240-movie fixture. Generated media and credentials are excluded from deliverables.

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

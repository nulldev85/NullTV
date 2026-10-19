# Changelog

## 0.6.1 — 2026-10-18

- Centered the current-time label directly above the pink now line.
- Aligned green live-program progress with the now line, including programs that begin before the visible schedule window.

## 0.6.0 — 2026-10-18

- Dracula styling now includes pill navigation across the top, refined channel cards, shared quality badges and clearer Settings panels.
- Guide has a fixed channel column, time-scaled program blocks, a scrolling six-hour timeline and a pink current-time line.
- Expand or retract the IPTV category rail with its arrow. Browse Favorites, Recently watched, All channels and categories grouped by provider.
- UHD/FHD/HD, frame-rate and HDR badges prefer measured stream information. Provider labels are identified in tooltips; unknown details stay unlabeled.
- Guide playback stays in its compact preview, with program summaries, fading controls, fullscreen and Escape restoration.

## 0.5.5 — 2026-10-18

- Player controls and stream details fade after inactivity and return with mouse, touch or keyboard activity.
- Fullscreen has a single exit-fullscreen control and an unobstructed video view; Escape restores the app.
- Stream changes no longer wait for server progress reporting. Playback loads are serialized, and native player cleanup stays off the interface thread.
- Refined navigation, lighter cards, aligned search/filter fields and a simpler Now Playing header give more room to the video.

## 0.5.4 — 2026-10-12

- Media Servers now opens a dedicated server library view with catalog names/order, Continue Watching, Next Up, collections, search, genre filters and paging. Jellyfin/AIOStreams libraries that require a genre are populated using their offered All/first genre instead of showing a connected server with zero items.
- Stream versions load when Play is pressed; browsing no longer resolves streams for every title. Server version names are selectable, and playback can return to the same server browser.
- Jellyfin login, playback and progress requests use the correct field names, with the selected version and session retained for progress sync.
- Volume and progress tracks meet their smaller green position markers without a gap.
- Guide has a thinner search/time toolbar, lighter frame and tighter preview spacing. The EPG reaches the content edges and bottom, with status information moved into the Guide heading.

## 0.5.3 — 2026-10-12

- Added separate Hide/Show menu and Hide/Show channels controls directly to Guide.
- Collapsing the channel column gives the schedule the full available width. Compact channel labels keep each row identifiable, and both controls remain available to restore the panels.
- Wider Guide layouts display more half-hour slots and load the corresponding programs from the existing EPG data, with matching time headings and paging.
- Guide uses the full content area when both panels are hidden, with a bounded preview and program summary beside it so more of the schedule remains visible. Playback, hover controls and fullscreen stay in Guide.

## 0.5.2 — 2026-10-12

- Added a navigation toggle in the app header on every tab. Guide, Movies & Series, Now Playing, Media Servers and Settings can collapse and reopen the sidebar without leaving their current page.
- Guide playback and its current search/order stay in place while the sidebar changes. Showing navigation from an expanded player restores the normal app view and reopens the sidebar.
- Removed duplicate navigation buttons from page headings and shortened update notes to app changes only.

## 0.5.1 — 2026-10-12

- Expanded and fullscreen players keep a compact Back and Close stream bar outside the video. Back restores the normal app view; Escape exits the current mode, including from focused player controls or text fields. Nested fullscreen returns to the preceding expanded view.
- Close stream remains visible in small Guide previews and available during loading, buffering and reconnect attempts. Now Playing also has a close action in its heading.
- Closing playback cancels pending loading/reconnection, stops all active tiles and picture-in-picture, saves progress and restores the Guide or normal player layout. Expanded viewing preserves navigation preferences; hover buttons indicate Restore app view and Exit fullscreen. Inactive transport cannot reopen a closed stream during focus/layout transitions; replay uses the explicit Play again action.

## 0.5.0 — 2026-10-12

- Added a dedicated Now Playing tab for live, movies, episodes and media-server playback. Guide retains its native preview; Live TV and Movies & Series are browsing views. Navigation shares the existing video session; Back to guide/library returns to browsing.
- Added a Dracula update dialog with installed/available versions, release notes, download progress/cancellation and a confirmed restart/update action. Startup checks run at most daily; Settings provides manual checks and an automatic-check preference.
- Stable releases come from the public NullTV-updates repository. A pinned ECDSA P-256 key authenticates exact release metadata; downloads verify SHA-256/length, MSI product family/version, ARM64 architecture and transactional rollback ordering. No GitHub credentials are embedded in the app.
- Added a separate bundled ARM64 updater helper, save-before-exit/relaunch behavior, read-sharing installer locks and recoverable update-result messages. The MSI registers its install directory and removes old versions inside the upgrade transaction so a failed installation restores them.
- Added maintainer signing/publication tooling and release documentation. Source pushes and CI artifacts do not automatically publish updates.

## 0.4.2 — 2026-10-12

- Reduced video overlay button, text and padding sizes. Controls now form a centered panel fitted to their contents; stream information also sizes to its text. Both use translucent Dracula surfaces over a transparent composition backdrop, with hover visibility retained.
- A single click on a currently airing Guide program starts its channel in the Guide preview. Upcoming/past cells retain program details; removed the old double-tap playback handler.
- Changed user-facing "programme/programmes" wording to "program/programs", including search, status, details, reminders and archive errors. XMLTV schema names and saved data models remain compatible.
- Playback position and volume use thin Dracula green tracks and slim markers while retaining native slider hit areas, dragging and keyboard input. The LIVE action has a matching green bar.
- Added small yellow accents to Guide's Now label and the stream information heading. Removed Full details and Fullscreen beside the Guide preview and gave its program summary the available height; fullscreen remains in the video controls.

## 0.4.1 — 2026-10-12

- Clicking a Guide channel starts a native preview beside the programme information, within Guide. Removed the separate row play icons and Play channel menu action.
- Guide fullscreen and window expansion use the same video session; Escape returns to the schedule with search, scroll and channel order retained. Browsing time windows no longer rebuilds the player or switches tabs. Channel stepping in Guide follows favorite order.
- Added compact hover controls and a compact stream panel for the Guide preview. Fullscreen retains the complete player controls, CC and LIVE action.
- Media Servers connector cards use equal rows and aligned, full-width action buttons. Bundled the official Jellyfin, Emby and Plex logo assets for offline display.

## 0.4.0 — 2026-10-12

- Added explicit channel play actions and programme double-tap playback to the favorite Guide. Removed the vertical time marker and retained each current programme's progress bar.
- Moved transport and seeking into native child XAML overlays above the video. Controls and the upper-right stream panel appear on video hover and hide on exit; keyboard/touch support remains available.
- Added VOD/media-server rewind/forward 10 seconds, timeline seeking, observed Play/Pause and a separate Stop action. Live supports pause and a LIVE action that reopens the current feed without a VOD start override.
- Added a visible CC control: Off, Automatic, embedded subtitle/closed-caption tracks and external subtitle files. Subtitles rise above visible controls and return to the preferred position when controls hide. Captions default to off; enabling/disabling persists and applies to channel changes. Secondary caption tracks are also disabled by Off.
- Stream information shows actual quality/resolution, codec/frame rate, hardware decode, audio codec/channels/sample rate, dropped frames and buffer duration.


## 0.3.1 — 2026-10-12

- Corrected the default to full Dracula Classic: #282A36 app background, #21222C surfaces and #343746 raised controls. Removed the black-background OLED variant.
- Existing OLED, Dark and Dracula OLED preferences migrate automatically to Classic while retaining encrypted sources, favorites, saved guide order and watch history.
- Empty player surfaces and the packaged Windows tile now use Dracula colors. About and diagnostics report the actual assembly version.


## 0.3.0 — 2026-10-12

- Guide now shows favorite live channels, including channels without programme data. Added logos/numbers, aligned time labels, a current-time marker, airing progress/remaining time, channel-name/programme search, captured drag grips and Move up/Move down actions.
- Added a programme information panel and full details with synopsis, local times, duration, genre, rating, episode, new/premiere/repeat information and reminders where supplied. XMLTV metadata survives encrypted guide caching; unavailable data is not fabricated.
- Guide order saves per profile, survives encrypted settings/backup round trips and preserves hidden channels when reordering search results. New favorites append after saved positions; removing a favorite removes its saved position.
- Movies and episodes open Now Playing within Movies & Series. Back to library and Now playing actions preserve library filters; Live TV remains the destination for live channels.
- Player visibility, dialog restoration and playback shortcuts now respect the library player.
- Applied the verified Dracula palette to shared inputs, menus, buttons, cards, dialogs and player controls. Purple actions/focus, yellow favorites/reminders and green live indicators; no cyan chrome. Classic Dracula and true-black Dracula OLED options are available; existing neutral OLED installs migrate to Dracula OLED.
- Retained the corrected Windows 11 registry prerequisite check and neutral installer artwork from installer 0.2.1.

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

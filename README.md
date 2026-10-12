# NullTV

Native Windows 11 ARM64 IPTV and media-server client for Surface Pro 11. Bring your own provider account, playlist, XMLTV guide or media server. No channel lists, sources, logins or content are included.

## Run

Run **NullTV-Setup-arm64.msi**, then launch **NullTV** from the Start menu. The per-user installer registers the app in Windows Settings → Apps and installs it under `%LOCALAPPDATA%\Programs\NullTV`. Close an older running build before installing. Your encrypted sources, profiles and history remain under `%LOCALAPPDATA%\NullTV`.

Version **0.4.2** makes video controls and stream information smaller, translucent and fitted to their content. Playback sliders use slim green tracks and markers, with a matching LIVE bar and small yellow accents. Click a currently airing program cell to play its channel in the Guide preview. Its summary uses the space beside the video; fullscreen remains in the video controls. The app uses “program/programs” throughout its interface. Fullscreen and Escape keep playback in Guide. The native installer is `NullTV-Setup-arm64-0.4.2.msi`.

The optional portable version is **NullTV-win-arm64.zip**: extract it completely and run `NullTV-win-arm64/NullTV.exe`. The folder contains the ARM64 .NET runtime, Windows App SDK runtime and `libmpv-2.dll`; no separate runtime installation is required. Windows 11 ARM64 is required. Keep every file beside the executable. Open **Settings → Sources → Add IPTV source**, enter your account or choose your local M3U/M3U8 file, and save. Click a channel in Live TV.

The optional MSIX is **unsigned**. A publisher must sign it using a certificate trusted by the device and matching `CN=NullTV` before `Add-AppxPackage` installation. The MSI is the primary ready-to-install deliverable. It does not require an MSIX signing certificate. It is currently unsigned, so Windows may show an unknown-publisher prompt.

## Build on the Surface

Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) for ARM64. In PowerShell, from this directory:

```powershell
./build.ps1 -Msix
```

The script restores pinned NuGet dependencies, downloads the pinned ARM64 libmpv archive, verifies SHA-256 and PE machine type, runs tests, publishes `win-arm64` self-contained output and ZIP, builds a per-user ARM64 MSI using pinned WiX 5.0.2 and UI extension 5.0.2, then optionally uses ARM64 MakeAppx from the Windows SDK NuGet package. MSI packaging also checks the actual registry search and prerequisite condition through a safe Windows Installer session; it does not install the app. No Visual Studio installation is needed. Windows `tar` extracts the `.7z` dependency. Downloads require NuGet and GitHub network access. Build tools may internally use an emulated x86/x64 XAML compiler; every app/runtime/playback executable delivered is checked for ARM64.

Outputs: `artifacts/NullTV-Setup-arm64.msi`, `artifacts/NullTV-win-arm64.zip`, `artifacts/NullTV-arm64.msix`. The app keeps encrypted data under `%LOCALAPPDATA%\NullTV`; rotating logs are in its `logs` subfolder. Copying portable binaries does not copy credentials. Use password-encrypted Backup / Restore to move settings between Windows users/devices.

## CI and repository

[NullTV repository](https://github.com/nulldev85/NullTV) · [Actions](https://github.com/nulldev85/NullTV/actions). Select a successful **Native ARM64 build** run and download the **NullTV-win-arm64** artifact (GitHub wraps the MSI, portable ZIP and optional MSIX in an artifact ZIP). The workflow uses `windows-11-arm` and supports push, pull request and manual dispatch. The repository contains the ordinary source tree; run `build.ps1` from its root.

## Architecture and engine

`src/NullTV.Core`: streaming M3U parser, bounded XMLTV reader with DTD disabled, Xtream and media server clients, models, DPAPI settings and AES-GCM backups. `src/NullTV.App`: WinUI shell, five functional views, native HWND video surfaces, thin C ABI libmpv wrapper, image cache, playback events and reconnect. `tests/NullTV.Tests`: dependency-free executable unit-test suite with provider-shaped synthetic JSON fixtures. `native/mpv.lock.json`: upstream release, asset, checksum and commit. `packaging`: optional MSIX manifest and logos.

WinUI 3 supports unpackaged ARM64 deployment, so no WPF fallback was used. The Windows App SDK is pinned at 1.8.260921001 with SDK BuildTools 10.0.26100.4654. Self-contained deployment follows [Microsoft's deployment guide](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps).

libmpv is the preferred engine: the [20261011 upstream release](https://github.com/shinchiro/mpv-winbuild-cmake/releases/tag/20261011) contains `mpv-dev-aarch64-20261011-git-accb7b2821.7z`; its SHA-256 is `174e0c5fb471cd6392fed4f6d3ad0d7dd33304aa5ea61b0d5955f2168624f391`. The DLL is machine `AA64`, not x64. It provides FFmpeg demuxing for HLS/MPEG-TS/RTSP/RTMP and H.264/HEVC/AV1; D3D11VA is selected by default, with software decode when unsupported. No separate codec pack or provider binaries are loaded. Built-in mpv configuration/scripts are disabled.

Tradeoffs: upstream Windows builds are community builds, and this pin is a development commit, not a stable mpv tag. GPU-next/D3D11 allows HDR output hints and hardware decode, but hardware support depends on the codec/profile, GPU driver, Windows HDR setting and source. HDR, passthrough, 120 Hz smoothness and battery consumption need validation on the specific Surface. Native child XAML islands place the controls and stream information above the Win32 video surface. They appear while hovering over the video and disappear on exit; keyboard activity reveals them briefly, and keyboard navigation retains visibility. Touch taps can operate the controls. Dialog display hides video and overlays while open. Media Foundation fallback and runtime LibVLC switching are not implemented.

## Security and behavior

Entire settings and channel-cache files are encrypted with current-user DPAPI; passwords, tokens and credential-bearing playlist/EPG addresses are masked in source forms. Backup uses PBKDF2-SHA256 (210,000 iterations), random salt/nonce and AES-256-GCM; lost passwords cannot be recovered. Logs record fixed event codes, exception types and method names only; exception messages and source file paths are excluded. Diagnostics exclude source addresses, names, user IDs, tokens, channel titles and credentials. Artwork is limited to 4 MiB per image, with four downloads in flight, shared requests, retry after transient errors and a memory cache that retains visible artwork. Periodic disk maintenance trims to 128 MiB / 500 entries.

ListView/GridView virtualize channels and posters. Playlist parsing and source refresh run off the UI thread. Energy Saver pauses automatic refresh; explicit refresh remains available. Offline catalogs and encrypted guide caches survive restart. Reconnect backs off 1, 2, 4, 8, 16, 30 seconds, then stops after six failures. Favorites and history are profile-specific. Catch-up requires an advertised archive window, matching provider EPG listing and a successful media response before playback; providers use differing timestamp conventions, so catch-up can still fail.

## Features and limitations

See [FEATURES.md](FEATURES.md) for the exact implemented/partial/deferred matrix and [CHANGELOG.md](CHANGELOG.md) for validation at each milestone. Server credentials were not supplied during development: integration behavior must be checked against your provider and your server versions. JSON fixtures are synthetic provider-shaped responses, not recordings of a real user's account. Reminders are in-app and require NullTV to remain running. Media servers direct-play only; DRM, transcoding negotiation and Plex server discovery are not implemented. No external content is bundled.

The default **Dracula** appearance uses the Classic #282A36 background throughout the app, with dark-purple surfaces and the verified purple accent. Earlier OLED/Dark/Dracula OLED settings automatically migrate to Classic; sources, favorites, guide order and history are preserved. Shared input/menu/dialog styles keep text readable; favorites/reminders use yellow and live status/progress uses green. Cyan chrome is omitted by request. Search fields, dropdowns and forms share input styling; hover feedback preserves each button’s shape. Click the star beside a channel or in the player to add a favorite. Guide shows favorite live channels, with logos/numbers, timed program cells, airing progress and provider-supplied details. Drag a channel’s grip or use its options to reorder; changes save per profile, including when searching. Channels with missing schedules remain available to watch.

Movies and episodes open **Now playing** within Movies & Series. **Back to library** restores your search and filters; **Now playing** returns to the player. Live and VOD use observed Play/Pause state with a separate Stop action. VOD/media-server playback has a seek bar and ±10 second actions. Live uses a green LIVE action that reopens the channel, discarding paused playback. The CC control offers Off, Automatic, embedded tracks and external SRT/ASS/SSA/VTT/SUB files; the enabled preference survives channel changes and restart. Captions are off by default. The heading buttons hide navigation or the channel pane; the player’s expand button fills the normal app window, and Escape restores the panes. The upper-right hover panel reports actual resolution/quality, codec/frame rate, decoding method, audio codec/channels/sample rate, dropped frames and buffered time. Screenshots in `docs/screenshots` show the native app using local synthetic artwork and generated playback media. No test source or media is bundled. Change the theme or accent under Settings → Appearance.

## Bug report

```text
NullTV version and artifact/run:
Windows build; Surface/GPU driver:
Source type (omit server URLs, credentials and private channel names):
Steps to reproduce:
Expected / actual behavior:
Codec / resolution / hardware decode from Playback → stream information:
Energy Saver / HDR / refresh rate:
Diagnostics ZIP (inspect before sharing):
```

## Licensing

App code: GPL-3.0-or-later. See LICENSE and THIRD-PARTY-NOTICES.md. libmpv and FFmpeg have their own copyleft and third-party obligations. Before redistributing native binaries, obtain and supply their complete corresponding sources and build materials as required by their licenses; dependency URLs alone are not a substitute. This developer MVP has not completed a redistribution-license audit.

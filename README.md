# NullTV

Native Windows 11 ARM64 IPTV and media-server client for Surface Pro 11. Bring your own provider account, playlist, XMLTV guide or media server. No channel lists, sources, logins or content are included.

## Run

Extract **NullTV-win-arm64.zip** completely and run `NullTV-win-arm64/NullTV.exe`. The folder contains the ARM64 .NET runtime, Windows App SDK runtime and `libmpv-2.dll`; no separate runtime installation is required. Windows 11 ARM64 is required. Keep every file beside the executable. Open **Settings → Sources → Add IPTV source**, enter your account or choose your local M3U/M3U8 file, and save. Click a channel in Live TV.

The optional MSIX is **unsigned**. A publisher must sign it using a certificate trusted by the device and matching `CN=NullTV` before `Add-AppxPackage` installation. The portable ZIP is the ready-to-run deliverable; no certificate trust changes are necessary for it.

## Build on the Surface

Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) for ARM64. In PowerShell, from this directory:

```powershell
./build.ps1 -Msix
```

The script restores pinned NuGet dependencies, downloads the pinned ARM64 libmpv archive, verifies SHA-256 and PE machine type, runs tests, publishes `win-arm64` self-contained output and ZIP, then uses ARM64 MakeAppx from the Windows SDK NuGet package. No Visual Studio installation is needed. Windows `tar` extracts the `.7z` dependency. Downloads require NuGet and GitHub network access. Build tools may internally use an emulated x86/x64 XAML compiler; every app/runtime/playback executable delivered is checked for ARM64.

Outputs: `artifacts/NullTV-win-arm64.zip`, `artifacts/NullTV-arm64.msix`. The app keeps encrypted data under `%LOCALAPPDATA%\NullTV`; rotating logs are in its `logs` subfolder. Copying portable binaries does not copy credentials. Use password-encrypted Backup / Restore to move settings between Windows users/devices.

## CI and repository

[NullTV repository](https://github.com/nulldev85/NullTV) · [Actions](https://github.com/nulldev85/NullTV/actions). Select a successful **Native ARM64 build** run and download the **NullTV-win-arm64** artifact (GitHub wraps the portable ZIP and optional MSIX in an artifact ZIP). The workflow uses `windows-11-arm` and supports push, pull request and manual dispatch. The repository contains the ordinary source tree; run `build.ps1` from its root.

## Architecture and engine

`src/NullTV.Core`: streaming M3U parser, bounded XMLTV reader with DTD disabled, Xtream and media server clients, models, DPAPI settings and AES-GCM backups. `src/NullTV.App`: WinUI shell, five functional views, native HWND video surfaces, thin C ABI libmpv wrapper, image cache, playback events and reconnect. `tests/NullTV.Tests`: dependency-free executable unit-test suite with provider-shaped synthetic JSON fixtures. `native/mpv.lock.json`: upstream release, asset, checksum and commit. `packaging`: optional MSIX manifest and logos.

WinUI 3 supports unpackaged ARM64 deployment, so no WPF fallback was used. The Windows App SDK is pinned at 1.8.260921001 with SDK BuildTools 10.0.26100.4654. Self-contained deployment follows [Microsoft's deployment guide](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps).

libmpv is the preferred engine: the [20261011 upstream release](https://github.com/shinchiro/mpv-winbuild-cmake/releases/tag/20261011) contains `mpv-dev-aarch64-20261011-git-accb7b2821.7z`; its SHA-256 is `174e0c5fb471cd6392fed4f6d3ad0d7dd33304aa5ea61b0d5955f2168624f391`. The DLL is machine `AA64`, not x64. It provides FFmpeg demuxing for HLS/MPEG-TS/RTSP/RTMP and H.264/HEVC/AV1; D3D11VA is selected by default, with software decode when unsupported. No separate codec pack or provider binaries are loaded. Built-in mpv configuration/scripts are disabled.

Tradeoffs: upstream Windows builds are community builds, and this pin is a development commit, not a stable mpv tag. GPU-next/D3D11 allows HDR output hints and hardware decode, but hardware support depends on the codec/profile, GPU driver, Windows HDR setting and source. HDR, passthrough, 120 Hz smoothness and battery consumption need validation on the specific Surface. The Win32 child video surfaces have an airspace constraint: player controls are below the video, and dialog display hides video while open. Fullscreen auto-hides the control strip; it is not an overlay composited over the native video. Media Foundation fallback and runtime LibVLC switching are not implemented.

## Security and behavior

Entire settings and channel-cache files are encrypted with current-user DPAPI; passwords, tokens and credential-bearing playlist/EPG addresses are masked in source forms. Backup uses PBKDF2-SHA256 (210,000 iterations), random salt/nonce and AES-256-GCM; lost passwords cannot be recovered. Logs record fixed event codes, exception types and method names only; exception messages and source file paths are excluded. Diagnostics exclude source addresses, names, user IDs, tokens, channel titles and credentials. Artwork is limited to 4 MiB per image and 128 MiB / 500 disk entries, with four downloads in flight.

ListView/GridView virtualize channels and posters. Playlist parsing and source refresh run off the UI thread. Energy Saver pauses automatic refresh; explicit refresh remains available. Offline catalogs survive restart. Reconnect backs off 1, 2, 4, 8, 16, 30 seconds, then stops after six failures. Favorites and history are profile-specific. Catch-up requires an advertised archive window, matching provider EPG listing and a successful media response before playback; providers use differing timestamp conventions, so catch-up can still fail.

## Features and limitations

See [FEATURES.md](FEATURES.md) for the exact implemented/partial/deferred matrix and [CHANGELOG.md](CHANGELOG.md) for validation at each milestone. Server credentials were not supplied during development: integration behavior must be checked against your provider and your server versions. JSON fixtures are synthetic provider-shaped responses, not recordings of a real user's account. Reminders are in-app and require NullTV to remain running. Media servers direct-play only; DRM, transcoding negotiation and Plex server discovery are not implemented. No external content is bundled.

The default OLED appearance uses a true-black background, warm charcoal panels, soft ivory text and a restrained champagne accent. Screenshots in `docs/screenshots` show the native app with no provider configured. Change the theme or accent under Settings → Appearance.

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

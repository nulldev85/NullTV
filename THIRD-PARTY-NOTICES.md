# Third-party notices

NullTV app source is licensed GPL-3.0-or-later. Native dependencies are not relicensed by this project.

| Dependency | Upstream | License / notes |
|---|---|---|
| .NET 8 | https://github.com/dotnet/runtime | MIT and third-party notices in runtime distribution |
| Windows App SDK / WinUI | https://github.com/microsoft/WindowsAppSDK | Microsoft distribution license and MIT portions; retain supplied notices |
| libmpv | https://github.com/mpv-player/mpv/tree/accb7b2821 | GPL-2.0-or-later in normal builds, with LGPL portions/options; see upstream Copyright and LICENSE files |
| FFmpeg and codec dependencies | https://github.com/FFmpeg/FFmpeg | LGPL/GPL configuration-dependent; included in upstream libmpv Windows binary |
| Windows build recipes | https://github.com/shinchiro/mpv-winbuild-cmake/tree/b2856ee | Includes dependency patches and configuration; check upstream release's build run |

Pinned release: https://github.com/shinchiro/mpv-winbuild-cmake/releases/tag/20261011
Pinned DLL archive SHA-256: 174e0c5fb471cd6392fed4f6d3ad0d7dd33304aa5ea61b0d5955f2168624f391

This file is a dependency inventory, not a completed license-compliance audit. The portable folder contains a community-built monolithic libmpv DLL. Its complete corresponding source set (mpv, FFmpeg, enabled codec/rendering libraries and patched build recipes) must be collected and supplied appropriately before redistribution. Do not assume that linking upstream repositories alone satisfies copyleft source obligations. No proprietary codec packs, content sources or provider accounts are included.

## Media server logos

Bundled only to identify compatible server connections; NullTV is independent of these projects. Marks remain the property of their respective owners.

- Jellyfin: https://jellyfin.org/images/logo.svg — official branding: https://jellyfin.org/docs/general/contributing/branding/
- Emby: https://emby.media/resources/logowhite_1881.png — https://emby.media/
- Plex: official inline SVG from https://www.plex.tv/ — https://www.plex.tv/about/privacy-legal/plex-trademarks-and-guidelines/

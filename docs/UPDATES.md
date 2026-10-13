# NullTV updates

The installed Windows app checks the stable feed once per day at startup. Settings → Updates provides manual checks and a startup-check switch. There is no continuous polling, automatic download, forced restart or embedded GitHub token.

Deliver future app updates through the built-in updater. Publish each signed release to the update repository, then direct the user to Settings → Updates. Do not present standalone installer downloads as the routine update path.

The public [NullTV-updates](https://github.com/nulldev85/NullTV-updates/releases) repository hosts installers, source archives and update.json. The development repository can stay private. Only published stable releases are offered; draft releases remain unavailable to the app. Keep the latest release published until its replacement is validated.

update.json contains an exact base64 JSON payload and an ECDSA P-256/SHA-256 signature. The verification key is pinned in UpdateTrust. The signed payload fixes the installer version, ARM64 architecture, minimum Windows build, download URL, byte length, SHA-256 and release notes. The client restricts URLs to versioned MSI assets in the release repository and verifies the actual MSI product family, version and ARM64 template. HTTPS is used for transport. Installer Authenticode signing is a separate future improvement.

Download progress appears inside the Dracula update dialog. A cancelled or invalid download cannot be offered for installation. Registered MSI installations offer Restart and update; portable copies offer the Windows installer. Playback progress/settings are saved before closing. A separate bundled ARM64 helper runs from the update cache, waits for the app to exit, authenticates and checks the installer again, holds a read-sharing lock while Windows Installer runs, prevents automatic Windows reboot, and reopens the installed app. Failures are recorded and shown on the next launch. Windows Installer owns MSI rollback; there is no custom file replacement or profile deletion.

## Publishing a release

1. Change Directory.Build.props, the package manifest, default MSI version and changelog together. Build and validate the native app and MSI. Each new build must have a higher three-part version.
2. Write short plain-text release notes describing app changes only. Keep test counts, verification logs, development history and build internals out of the in-app update notes; maintain those separately. Package the matching source ZIP, including scripts, source, licenses, dependency lock files and this document. No signing key, provider data or credentials belong in an archive.
3. Use scripts/release.ps1 with Version, InstallerPath, SourceArchive, NotesPath and SigningKeyPath. It signs the manifest, verifies MSI/hash/signature, and creates a draft with the MSI, source ZIP, manifest and checksum list. Inspect the draft and publish with gh release edit or pass -Publish only for an approved, validated release.
4. Verify the anonymous latest/download/update.json endpoint and its installer after publication. Keep existing versions available. Ordinary source pushes and CI artifacts do not publish updates.

The release signing key is stored outside the source tree as a DPAPI-encrypted file, readable only by its Windows user. NullTV.Release keygen creates a new key and prints only its public key; the initial public key is already pinned. Preserve/back up the encrypted key with its Windows profile. Do not regenerate it for each build. Losing this key requires a separately installed trust-key migration release. Publication uses the maintainer's authenticated gh CLI on the build machine; nothing gives the app repository-write access. A CI publisher can be added later with scoped repository credentials and a protected approval environment.

## Validation modes

Core tests use synthetic signed manifests and in-memory HTTP fixtures. WindowsUpdatePackage only reads MSI metadata, without running its actions. An isolated --data-dir with --smoke-update-current <older version> exercises the real published release prompt and download; installation is disabled for that validation flag. Native startup smoke tests skip automatic network checks. None of these modes modifies an installed user's app or profile.

References: [GitHub Releases API](https://docs.github.com/en/rest/releases/releases), [Windows Installer major upgrades](https://learn.microsoft.com/en-us/windows/win32/msi/applying-major-upgrades-by-installing-the-product), [.NET single-file deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview).

# Code signing policy

**Status:** application to SignPath Foundation pending. Releases are currently unsigned; every release publishes a SHA-256 checksum next to the installer and the in-app updater verifies it before installing.

## Team

| Role | Member |
|---|---|
| Committer / Reviewer / Approver | Koray Orhun ([@korayorhun](https://github.com/korayorhun)) |

Sesli Okuma is maintained by a single developer. All commits, reviews and signing approvals are performed by that maintainer using a GitHub account protected with multi-factor authentication.

## Build

- Source: https://github.com/korayorhun/SesliOkuma (MIT).
- Every push and tag is built on GitHub-hosted runners by [`.github/workflows/build.yml`](../.github/workflows/build.yml): `build.ps1` compiles `src\*.cs` with the C# compiler shipped in .NET Framework 4.8, then Inno Setup 6 produces `dist\SesliOkuma-Setup-<version>.exe`. No third-party binaries are bundled.
- Release artifacts are produced from tagged commits only; signing requests (once available) are approved manually per release by the maintainer.

## Artifacts to be signed

- `SesliOkuma.exe` — product name *Sesli Okuma*, version = release tag.
- `SesliOkuma-Setup-<version>.exe` — Inno Setup installer (and its uninstaller).

## Privacy

Sesli Okuma collects no data. The optional update check requests `https://api.github.com/repos/korayorhun/SesliOkuma/releases/latest` once a day; the optional support/voice-catalog lookups read static files from this repository. Text you read aloud is passed only to the Windows speech provider (SAPI) you select.

## Statement (to be added on approval)

> Free code signing provided by [SignPath.io](https://signpath.io), certificate by [SignPath Foundation](https://signpath.org).

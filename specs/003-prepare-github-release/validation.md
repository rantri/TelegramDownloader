# Public release validation

Verified on 2026-11-18 with Windows x64 and .NET SDK 11.0.100.

## Contributor checks

- Cloned public `main` into an empty directory and ran `./scripts/verify.ps1`. Locked restore, Release solution and GUI harness builds, all **182 tests**, single-file publish, help and version checks passed. Builds reported zero warnings and errors. The checkout stayed clean.
- The package script rejected `v0.0.0` against application version 2.0.0. Packaging with `v2.0.0` succeeded. The local archive digest matched its manifest and the extracted executable ran.
- GitHub tested revisions `41c25742b725` and `4f74ee52c922` independently in the same branch push. It also tested proposed merge `ab0a1eb82edf` and completed the stable `CI passed` check. [Batch push](https://github.com/rantri/TelegramDownloader/actions/runs/44394545803) · [PR merge check](https://github.com/rantri/TelegramDownloader/actions/runs/44394599636) · [Merged contributor templates](https://github.com/rantri/TelegramDownloader/pull/1)
- Private vulnerability reporting and Dependabot security updates are enabled. The public issue and private advisory entry points take unauthenticated visitors to GitHub sign-in.

## Release acceptance

| Check | Observed result |
| --- | --- |
| Tagged source | `v2.0.0` identifies `d66627e8dacd6d2676aa7d1da8a5e9ea6896db44`. [Main CI](https://github.com/rantri/TelegramDownloader/actions/runs/44394944362) |
| Publication | The tagged Windows build passed verification and packaging. The separate publication job uploaded both assets and made the draft public. [Release workflow](https://github.com/rantri/TelegramDownloader/actions/runs/44395234816) |
| Acquisition | An anonymous download of both assets from the public release succeeded. The ZIP was 50,944,823 bytes. [Release and notes](https://github.com/rantri/TelegramDownloader/releases/tag/v2.0.0) |
| Integrity | The ZIP SHA-256 was `b002e4bfb1a753d95601d34569664eeedb6fcca3e95b06694aed889fe53a1152`. It matched the published `SHA256SUMS.txt` and GitHub's asset digest. |
| Contents | Nine nonempty files included the executable, project MIT license, attribution index and six upstream license or notice files. |
| Startup | The extracted executable printed `TelegramDownloader 2.0.0` and returned download usage for `--help`. Both commands exited successfully. Windows file version was `2.0.0.0`. |

## Privacy and scope

Private post references and live download details were removed from source, examples and prior validation records. The public baseline has a new root. Thirteen earlier development commits remain in a local archive.

This release exercised packaging and account-free startup. Live Telegram sign-in, a real download, and interactive tray behavior were outside this release check. The [earlier application validation](../002-parallel-downloads-naming/validation.md) records separate functional verification.

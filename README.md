# TelegramDownloader

[![CI](https://github.com/rantri/TelegramDownloader/actions/workflows/ci.yml/badge.svg)](https://github.com/rantri/TelegramDownloader/actions/workflows/ci.yml)

A Windows x64 tool that saves the original video attached to a Telegram post link.
One executable provides a command line and a desktop window. It uses your own Telegram
account and downloads the exact linked video without converting it or downloading the
rest of an album. The desktop window queues up to three simultaneous downloads. Choose a
destination folder; filenames come from the original file or post, with scene-style names
for recognizable TV episodes.

## Get the Windows app

1. Open the [latest release](https://github.com/rantri/TelegramDownloader/releases/latest).
   Under **Assets**, download `TelegramDownloader-<version>-win-x64.zip` and `SHA256SUMS.txt` into the same folder.
2. Open PowerShell in that folder. This example verifies version 2.0.0. Change the version to match your download.

   ```powershell
   $version = '2.0.0'
   $zip = "TelegramDownloader-$version-win-x64.zip"
   $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
   if ((Get-Content -LiteralPath SHA256SUMS.txt -Raw).Trim() -ne "$hash  $zip") {
       throw 'Checksum mismatch. Download both files again from the same release.'
   }
   Expand-Archive -LiteralPath $zip -DestinationPath "TelegramDownloader-$version"
   Set-Location "TelegramDownloader-$version"
   .\TelegramDownloader.exe --version
   ```

3. Double-click `TelegramDownloader.exe` to open the window, then choose **Set up account**.
   For terminal use, follow [Set up your account](#set-up-your-account). The release includes
   the .NET runtime. You only need the SDK if you want to build from source.

## Build and publish

Development requires Windows x64 and .NET SDK **11.0.100** (or an SDK patch accepted by
`global.json`). Package restore needs internet access. Run these commands from the repository:

```powershell
dotnet restore TelegramDownloader.slnx --locked-mode
dotnet build TelegramDownloader.slnx -c Release --no-restore
dotnet test TelegramDownloader.slnx -c Release --no-build
./scripts/publish.ps1
```

The result is `artifacts/publish/win-x64/TelegramDownloader.exe`. Copy that executable
wherever you want to run it on this PC. It includes the .NET runtime and Windows Forms;
the destination PC does not need a separate application runtime installation.

The script works from any working directory. To choose an output directory:

```powershell
./scripts/publish.ps1 -OutputDirectory 'C:\Tools\TelegramDownloader'
```

A relative output path is relative to your current directory. The directory must be empty
or contain only a previous `TelegramDownloader.exe`, which publishing replaces. The script
does not delete files, rejects unrelated contents, and verifies that publication produces
exactly one nonempty executable. It does not use trimming or native AOT.

The executable embeds native runtime libraries, which .NET extracts on launch, normally
under `%TEMP%\.net`. `DOTNET_BUNDLE_EXTRACT_BASE_DIR` can select another extraction base.
This runtime cache is separate from account storage. See Microsoft's
[single-file deployment documentation](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview).

## Set up your account

1. Sign in to [Telegram's API development tools](https://my.telegram.org/apps) with your
   existing Telegram account. Register your own application and obtain its `api_id` and
   `api_hash`. Telegram describes the process in its
   [application setup guide](https://core.telegram.org/api/obtaining_api_id).
2. Open a terminal in the folder containing `TelegramDownloader.exe`, then run:

   ```powershell
   ./TelegramDownloader.exe configure
   ./TelegramDownloader.exe login
   ```

3. Enter the API ID, API hash, and phone number including its international `+` prefix.
   Complete the verification code and, if requested, your two-step verification password.
   Secret prompts are masked. Enter these values in the prompts, not command arguments
   or repository files.

This is an independent Telegram sign-in. Telegram Desktop may receive the verification
code, but this tool does not read or import Desktop session files. Once signed in, the
saved session is reused until Telegram expires or revokes it. Interactive prompts require
a terminal; redirected input fails instead of waiting for credentials.

## Download from the command line

```powershell
# Choose an existing folder. The filename is selected automatically.
./TelegramDownloader.exe download 'https://t.me/c/9000000001/42' "$env:USERPROFILE\Videos"

# Replace the automatically named file only when explicitly requested.
./TelegramDownloader.exe download 'https://t.me/c/9000000001/42' "$env:USERPROFILE\Videos" --overwrite

./TelegramDownloader.exe --help
```

The sample link demonstrates the accepted format; your account must have access to the
actual post. Public `https://t.me/<username>/<message>` links and private `/c/` links are
supported, including topic paths. The final path number identifies the exact message.
Invitation and comment links are not supported.

**Version 2.0 changes the destination argument to an existing folder only.** Exact filename
arguments and nonexistent folders are rejected with exit code 5 before connecting to Telegram.
Update older scripts to pass the containing folder. Saved account settings and sessions use
the same format, so no new sign-in is required solely because of this upgrade.

Names are sanitized for Windows while retaining Unicode and the media extension. A clear
episode identity in the original filename takes priority over conflicting post text. Otherwise,
a clear title, season, and episode from the post can supply the name; then a useful original
filename, useful post title, or generated fallback is used. Ambiguous or incomplete episode
identities keep a safe name without guessing. Recognized episodes use `Show.Name.S01E02` and
retain supplied release details. Missing season, episode, quality, codec, or release group
values are never invented. Examples:

| Original filename | Post text, when needed | Saved name |
|---|---|---|
| `ornek_S01E11.mp4` | — | `ornek.S01E11.mp4` |
| `Example Show 1x2 1080p WEB-DL.mkv` | — | `Example.Show.S01E02.1080p.WEB-DL.mkv` |
| `video_42.mp4` | דוגמה עונה 1 פרק 11 | `דוגמה.S01E11.mp4` |
| `clip.mp4` | Episode 11 | `clip.mp4` |

Without enough episode information, a safe original filename or useful post title is kept;
the final fallback is `video-<message-id>` with the known media extension, or `.bin` if unknown.
Existing files receive a numeric suffix such as `Show.S01E02 (2).mkv`. Parallel jobs reserve
different names. Pass `--overwrite` to replace the automatically chosen base name only after
a successful download. Failed or cancelled replacements preserve the original file, and two
jobs cannot replace the same path simultaneously.

Progress and errors go to standard error. Success prints the completed absolute path to
standard output. Press **Ctrl+C** to cancel. Transfers use temporary sibling files; a file
is promoted to the final destination only after completion and expected-size verification.
Partial files are cleaned up when possible. The CLI remains a single foreground download;
use the GUI to queue multiple jobs. Downloads are not resumable.

| Exit code | Meaning |
|---|---|
| 0 | Success |
| 1 | Unexpected failure |
| 2 | Invalid input |
| 3 | Authentication or configuration required |
| 4 | Missing, inaccessible, or non-video post |
| 5 | Destination or download-integrity error |
| 6 | Network failure or Telegram service wait |
| 7 | Account in use by another instance |
| 130 | Cancelled |

## Download through the window

Double-click `TelegramDownloader.exe`, run it without arguments, or run:

```powershell
./TelegramDownloader.exe gui
```

Use **Set up account** and **Sign in** for the same account setup. Paste a post link, choose
an existing **Destination folder**, then choose **Add download**. Add more links while work
continues; three jobs run together and additional jobs wait in order. The folder selection
stays available for the next download. Enable **Replace** only to replace an existing file.

Each row shows the filename or post link, state, progress, transferred bytes, and destination.
Select a row for its complete link, output path, and any error. **Cancel selected** cancels
only that job, leaving other downloads running. A transfer can remain **Cancelling** while
its current network request finishes. **Retry selected** adds a fresh job for a failed or
cancelled download and keeps the original result visible. Account actions become available
again when all jobs and session cleanup are finished.

Minimizing keeps downloads running. Closing the window while busy hides it to the system
tray and shows a notification; use the tray icon's **Show** action or double-click it to
restore the window. **Exit**, in the window or tray menu, cancels pending and active jobs,
cleans up incomplete files, and closes the session before the process exits. Closing an idle
window exits normally. The queue exists only for the current process: there is no background
Windows service, automatic startup, or queue restoration after exit.

## Account storage and removal

Configuration and sessions are stored under `%LOCALAPPDATA%\TelegramDownloader`, encrypted
with Windows DPAPI for the current user and protected by directory access controls. Moving
the executable does not move or reset that account state. Verification codes and passwords
are not saved. Diagnostics omit secrets and private message content.

To remove this tool's local account settings and session, close other running operations and use:

```powershell
./TelegramDownloader.exe logout
```

The GUI offers **Forget local account** for the same action. Downloaded videos remain in
their chosen destinations. Local removal does not revoke the server-side authorization;
use Telegram **Settings > Devices** if you also want to terminate that authorization.

If the saved session is damaged or its authorization has expired, run `logout`, `configure`,
then `login`. In the window, use **Forget local account**, **Set up account**, then **Sign in**.
For a temporary Telegram error or flood wait, keep the saved account and retry after the wait.

The GUI queue shares one Telegram session while jobs are running and releases its account
lock when idle. The CLI and GUI share the saved account; a second busy instance reports an
account-busy error instead of writing the same session concurrently.

## Availability and verification

Private posts require access through the signed-in account. The tool attempts direct video
retrieval even when Telegram Desktop disables saving, but Telegram may refuse the request.
Disabled-save retrieval is not guaranteed. Deleted posts, posts without video, access refusals,
and service waits produce errors; long Telegram waits are reported for a later retry.

Automated tests use synthetic data and do not require a Telegram account. The owner confirmed
successful sign-in and a private-post download; its local MP4 output and container structure
were inspected. Other live scenarios, including protected-content status and cancellation,
remain separately listed in [the original validation record](specs/001-download-telegram-videos/validation.md).
Follow [the parallel-download and naming validation guide](specs/002-parallel-downloads-naming/quickstart.md)
to check the new queue, naming, and tray behavior with your own account without placing
credentials or private media in test fixtures.

## Third-party notices

TelegramDownloader is available under the [MIT license](LICENSE). Downloaded release
packages include the license and [third-party notices](licenses/README.md) alongside the executable.

- **WTelegramClient 4.4.10** — Copyright (c) 2021–2026 Olivier Marcoux. Licensed under the
  [MIT License](https://github.com/wiz0u/WTelegramClient/blob/master/LICENSE.txt).
  [Project and source](https://github.com/wiz0u/WTelegramClient).
- **.NET runtime and System.Security.Cryptography.ProtectedData** — Copyright (c) .NET
  Foundation and Contributors. [MIT License](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT)
  and [third-party notices](https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT).
- **Windows Forms** — Copyright (c) .NET Foundation and Contributors.
  [MIT License](https://github.com/dotnet/winforms/blob/main/LICENSE.TXT).

The published executable includes these components. Their upstream license and notice files
describe the respective permissions and attribution requirements.

## Contributing

The [contributor guide](CONTRIBUTING.md) covers the local verification command, optional
GUI smoke check, pull requests and release procedure. Use [Issues](https://github.com/rantri/TelegramDownloader/issues/new/choose)
for bugs and feature requests. Follow the [security policy](SECURITY.md) to report a vulnerability privately.

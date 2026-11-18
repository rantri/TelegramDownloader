# Implementation Plan: Download Telegram Videos

**Branch**: `main` | **Date**: 2026-11-13 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/001-download-telegram-videos/spec.md`

## Summary

Build one Windows executable with command-line commands and a Windows Forms window.
Both use a shared download service and WTelegramClient adapter. Downloads stream to a
temporary sibling file, verify expected length, then move into place. Account settings
and sessions use Windows user encryption and restricted local storage.

## Technical Context

**Language/Version**: C# on .NET 11; SDK 11.0.100 is installed and will be pinned.
**Primary Dependencies**: Windows Forms; WTelegramClient 4.4.10;
System.Security.Cryptography.ProtectedData 11.0.0.
**Storage**: DPAPI CurrentUser files under LocalApplicationData/TelegramDownloader;
no database. Destination supplied by user.
**Testing**: xUnit with synthetic gateways/streams, Windows storage tests, CLI process
smoke checks, GUI launch checks, and separately documented live-account checks.
**Target Platform**: Windows x64 with .NET 11 support.
**Project Type**: Desktop and CLI application in one executable.
**Performance Goals**: Stream rather than buffer whole videos; responsive GUI; synthetic
cancellation under two seconds; progress updates throttled by the presentation layer.
**Constraints**: No saved login codes/passwords; no Desktop session extraction; bounded
connection retries; service flood waits surfaced rather than automatically retried.
**Scale/Scope**: One account, one operation at a time, one exact linked video per request.

Package lock files are committed. The shared win-x64 runtime identifier and single-file
analyzer keep the restore graph consistent between ordinary builds/tests and publishing.

## Constitution Check

Pre-research and post-design gates: PASS.

- Personal-use simplicity: two production projects and one test project; no hosted service,
  queue, database, dependency injection framework, or speculative extension architecture.
- .NET 11 default: net11.0-windows and pinned SDK; no exception required.
- Credential privacy: DPAPI CurrentUser plus directory ACL; masked input; disabled
  library logging; synthetic tests. Session callback errors must be surfaced.
- Download integrity: unique partial file, expected size, flush/close before move,
  no overwrite without explicit choice, cancellation drains/stops writers before cleanup.
- Verification: tests for parser, state/storage, failure mapping, completion/collision/
  cancellation; builds and publication smoke checks; live checks marked unverified until run.

## Project Structure

### Documentation (this feature)

```text
specs/001-download-telegram-videos/
  spec.md
  checklists/requirements.md
  plan.md
  research.md
  data-model.md
  quickstart.md
  contracts/interfaces.md
  tasks.md
  validation.md
```

### Source Code (repository root)

```text
global.json
Directory.Build.props
TelegramDownloader.slnx
src/TelegramDownloader.Core/
  TelegramDownloader.Core.csproj
  Contracts.cs
  PostLink.cs
  DownloadService.cs
  AppProfile.cs
  ProfileStore.cs
  TelegramGateway.cs
src/TelegramDownloader.App/
  TelegramDownloader.App.csproj
  Program.cs
  Cli.cs
  MainForm.cs
  PromptDialog.cs
tests/TelegramDownloader.Tests/
  TelegramDownloader.Tests.csproj
  PostLinkTests.cs
  DownloadServiceTests.cs
  ProfileStoreTests.cs
  TelegramErrorTests.cs
scripts/publish.ps1
README.md
```

**Structure Decision**: The core owns download integrity, link interpretation, Telegram
interaction, and private storage. The application owns presentation and prompts. One
ITelegramGateway boundary enables account-free tests without a proliferation of services.

## Complexity Tracking

No constitution violations or exceptions.

## Implementation Decisions

- Console-subsystem executable with WinForms: arguments select CLI; no arguments or
  `gui` opens the window and detaches its console. Preserve reliable CLI output/exit codes.
- WTelegram login uses a synchronous prompt callback on a worker; the GUI marshals a
  masked prompt dialog onto its UI thread. Cancellation closes prompts and disposes the
  network client; await underlying operations before disposing streams.
- Resolve public channels with Contacts_ResolveUsername, private channels using joined
  chat access hashes. Use Channels_GetMessages for the exact message; reject unsupported
  comment links. Channel IDs above the signed 32-bit range are supported.
- Accept video MIME or video attributes. Download the original document. Do not invent a
  client-side ban merely because Desktop hides save controls; report actual server refusal.
- Disable library logging; map known RPC failures to safe user messages. No automatic flood
  waits; configure both main and media-DC clients. Bound reconnect attempts.
- Transfer sequential 512 KiB getFile chunks with awaited writes so cancellation cannot leave
  parallel writers behind. Refresh an expired file reference once, checking media identity/size.
- A process-independent exclusive file lease protects account state during operations.
  Atomic DPAPI writes and explicit persistence-error checks prevent false saved-login claims.
- Single-file self-contained win-x64 publication embeds native runtime files for extraction.
  No trimming/AOT; include third-party notices in documentation and executable help.

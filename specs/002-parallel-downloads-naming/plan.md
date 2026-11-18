# Implementation Plan: Parallel Downloads and Automatic Names

**Branch**: `main` | **Date**: 2026-11-13 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/002-parallel-downloads-naming/spec.md`

## Summary

Extend the current Windows app with a bounded in-memory download queue, a shared account
session per busy batch, independent job cancellation, and tray background behavior.
Replace exact output filenames with folders in both interfaces; derive conservative
scene-style names from original metadata/caption and reserve collision-safe output paths.

## Technical Context

**Language/Version**: C#, .NET 11, pinned SDK 11.0.100; app version 2.0.0 for changed CLI destination semantics.
**Primary Dependencies**: Existing Windows Forms, WTelegramClient 4.4.10, DPAPI 11.0.0; no new packages.
**Storage**: Existing protected account state; queue and caption metadata in memory only.
**Testing**: Existing xUnit suite plus gated fake gateways/transports, filename tables,
concurrent filesystem tests, published CLI checks, and GUI/tray inspection.
**Target Platform**: Windows x64, self-contained single EXE.
**Project Type**: Desktop + CLI application using existing two production projects.
**Performance Goals**: Three overlapping jobs; UI updates at least every 250 ms during
controlled transfers; no full-video buffering.
**Constraints**: Per-job cancellation is cooperative at awaited RPC boundaries; explicit
shutdown disposes the whole client and drains all writers. Never log/persist full captions.
**Scale/Scope**: One account and one process owning its session at a time; in-memory queue
for this app session, not a service or restart-resumable queue.

## Constitution Check

Pre-research and post-design: PASS.

- Simplicity: add queue/namer/path-reservation components only for requested behavior;
  keep production projects and dependencies unchanged. A nonshipping GUI smoke harness
  links the actual form and injects synthetic transfers to verify background lifecycle.
- .NET 11: retain existing pin/runtime/deployment graph.
- Privacy: one protected session owner per batch, no copied credentials, transient captions;
  queue errors use existing safe categories.
- Integrity: keep sequential chunks per file, exact-size checks and atomic final moves;
  reserve active paths case-insensitively and suffix collisions. Explicit overwrite is opt-in.
- Verification: write focused tests before changing auth/lifecycle/naming/filesystem behavior;
  use synthetic data for routine checks and report any live tests separately.

## Project Structure

### Documentation (this feature)

```text
specs/002-parallel-downloads-naming/
  spec.md
  checklists/requirements.md
  plan.md
  research.md
  data-model.md
  contracts/interfaces.md
  quickstart.md
  tasks.md
  validation.md
```

### Source Code (repository root)

```text
src/TelegramDownloader.Core/
  Contracts.cs                 # transient naming metadata and progress name
  TelegramGateway.cs           # shared auth, parallel RPCs, isolated cancellation
  VideoFileNamer.cs             # pure bounded naming rules
  OutputPathReservation.cs     # in-process case-insensitive active paths
  DownloadService.cs           # folders, naming, collision-safe promotion
  DownloadQueue.cs             # scheduler, snapshots, retry, shutdown
src/TelegramDownloader.App/
  MainForm.cs                  # queue grid, folder picker, tray lifecycle
  Cli.cs                       # folder-only validation/help, version
  TelegramDownloader.App.csproj
tests/TelegramDownloader.Tests/
  AuthenticationTests.cs
  ParallelGatewayTests.cs
  VideoFileNamerTests.cs
  DownloadServiceTests.cs
  DownloadQueueTests.cs
tests/TelegramDownloader.GuiSmoke/ # isolated native lifecycle checks; no live account
README.md
scripts/publish.ps1             # existing verified publisher
```

**Structure Decision**: UI observes immutable queue snapshots rather than owning workers.
The queue creates one gateway per active batch and releases it on idle. Naming and path
allocation are shared by GUI and CLI; the Telegram adapter remains the only account boundary.

## Complexity Tracking

No constitution exceptions.

## Design Decisions

- DownloadQueue accepts a gateway factory. It starts at most three jobs, keeps insertion
  order for waiting jobs, and snapshots state under a lock. Each job owns a cancellation
  source. Failed/cancelled retries create new jobs; history stays until app exit.
- A single scheduler owns batch lifecycle. It disposes the shared gateway when idle before
  ending the runner; additions during disposal are handled in a new batch. Busy includes
  cleanup so account setup/removal cannot race session ownership.
- TelegramGateway caches one shared login task, uses its own lifetime token for configuration,
  and tracks admitted operations. Job cancellation does not dispose the client or release its
  lease. Failed initialization releases ownership; normal job errors leave siblings running.
- Await each RPC/write; after an in-flight request returns, check the job token before writing.
  Serialize media-DC create/configure/connect so flood/reconnect policies apply before use.
  Whole-owner disposal is idempotent, stops new operations, breaks RPC waits, drains admitted
  work, and then releases the lease.
- Retain sequential 512 KiB reads per file, bounded reference refresh/reconnect behavior,
  and error handling. Concurrency is across videos.
- VideoInfo gains optional transient PostText/MimeType. VideoFileNamer prioritizes original
  episode identity, then explicit caption identity, then usable original/caption/fallback.
  Recognized original suffixes survive; unknown quality/source/group are never invented.
- DownloadService requires an existing folder, reserves a sanitized candidate path, and
  reports its chosen name with progress. Existing/reserved collisions use numbered suffixes;
  a final non-overwrite race allocates another safe suffix and retries the atomic move.
  Explicit overwrite never claims an active reserved name.
- MainForm uses a queue grid and a 250 ms refresh timer. Link/folder/Add remain usable during
  downloads; Cancel/Retry target the selected row. Account actions are disabled while busy.
  Minimize continues work; close while busy hides to tray; tray Show restores; tray/form Exit
  cancels/drains. Closing idle exits. No queue persistence or auto-start.
- CLI stays one foreground download command but accepts folders only, uses the same naming
  and collision policy, and preserves --overwrite. On Ctrl+C it disposes its owned gateway
  so a single foreground command can stop pending RPCs promptly.

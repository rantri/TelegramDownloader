# Tasks: Parallel Downloads and Automatic Names

**Input**: Design artifacts in `specs/002-parallel-downloads-naming/`
**Prerequisites**: spec.md, plan.md, research.md, data-model.md, contracts/interfaces.md
**Tests**: Required by FR-014/constitution V; use gated synthetic work and temporary directories.
**Organization**: Shared contracts, parallel core/presentation work, integration and publication.

## Format: `[ID] [P?] [Story] Description`

Paths are repository-relative. [P] work owns disjoint files after its dependencies complete.

## Phase 1: Setup (Shared Infrastructure)

- [x] T001 Verify .NET 11 dependency/ignore setup and update src/TelegramDownloader.App/TelegramDownloader.App.csproj to version 2.0.0 for folder-only CLI behavior.

## Phase 2: Foundational (Blocking Prerequisites)

- [x] T002 Extend src/TelegramDownloader.Core/Contracts.cs with optional transient PostText/MimeType and progress filename; define queue snapshot boundary in src/TelegramDownloader.Core/DownloadQueue.cs per contracts/interfaces.md (FR-002, FR-007, FR-013).

## Phase 3: User Story 1 - Parallel Queue (Priority: P1)

**Goal**: Three real overlapping jobs with independent state/cancellation/retry.
**Independent Test**: Gated fakes prove overlap, cap/order, individual cancellation, retry,
shared login, failure isolation, and lease release without an account.

### Tests for User Story 1

- [x] T003 [P] [US1] Write tests/TelegramDownloader.Tests/DownloadQueueTests.cs for overlap/cap/order, enqueue while busy, queued/running cancellation, retry, ordinary failures, idle disposal and enqueue during cleanup; "At most MaximumConcurrency jobs are admitted at once" (FR-001–FR-004, FR-012, FR-014; SC-001, SC-002).
- [x] T004 [P] [US1] Write tests/TelegramDownloader.Tests/ParallelGatewayTests.cs and update AuthenticationTests.cs for one shared login, first-waiter cancellation isolation, concurrent operations, job failure isolation, media initialization policy, idempotent shutdown/drain and lease ownership (FR-003, FR-012–FR-014).

### Implementation for User Story 1

- [x] T005 [US1] Refactor src/TelegramDownloader.Core/TelegramGateway.cs for shared lifetime auth, admitted-operation tracking, independent cooperative cancellation, serialized media initialization and whole-owner draining disposal; retain bounded reconnects and safe persistence/error handling (FR-003, FR-012, FR-013).
- [x] T006 [US1] Implement src/TelegramDownloader.Core/DownloadQueue.cs scheduler, immutable snapshots, per-job tokens, retry, batch idle cleanup and safe SessionError (FR-001–FR-004, FR-012).
- [x] T007 [P] [US1] Update src/TelegramDownloader.App/MainForm.cs with a responsive queue grid, usable Add inputs, per-item progress/error/path details and Cancel/Retry actions (FR-001–FR-004; SC-002).

## Phase 4: User Story 2 - Folder and Automatic Names (Priority: P1)

**Goal**: Shared deterministic names and safe paths in GUI and CLI.
**Independent Test**: Synthetic naming table and concurrent file tests; CLI rejects filenames.

### Tests for User Story 2

- [x] T008 [P] [US2] Add tests/TelegramDownloader.Tests/VideoFileNamerTests.cs and update DownloadServiceTests.cs for precedence, conflicting/incomplete markers, English/Hebrew names, multi-episodes, sanitization/extension, folder-only input, active/existing/external-race collisions and failed replacement (FR-006–FR-011, FR-014; SC-003).

### Implementation for User Story 2

- [x] T009 [US2] Implement src/TelegramDownloader.Core/VideoFileNamer.cs: "Season and episode numbers must be explicit; never infer season one"; "Season zero is allowed for specials"; "Unicode titles and media extensions must survive sanitization" (FR-007–FR-010).
- [x] T010 [US2] Implement src/TelegramDownloader.Core/OutputPathReservation.cs and adapt DownloadService.cs to folders, generated-name progress, numbered suffixes, atomic race handling and opt-in replacement; "Active paths compare case-insensitively" (FR-006, FR-010, FR-011).
- [x] T011 [US2] Pass exact message caption/MIME transiently from src/TelegramDownloader.Core/TelegramGateway.cs; "PostText is transient and must not be logged or persisted" (FR-007, FR-013).
- [x] T012 [US2] Replace file picker with folder picker in src/TelegramDownloader.App/MainForm.cs and update Cli.cs folder validation/help/version and owned-client Ctrl+C shutdown (FR-006, FR-013).

## Phase 5: User Story 3 - Background Lifecycle (Priority: P2)

**Goal**: Hide/restore active work; explicit exit stops all writers safely.
**Independent Test**: Synthetic shutdown tests plus native GUI/tray checks.

- [x] T013 [US3] Add shutdown/admission/cancellation/drain cases to tests/TelegramDownloader.Tests/DownloadQueueTests.cs before implementing full queue exit behavior in src/TelegramDownloader.Core/DownloadQueue.cs (FR-005, FR-012, FR-014; SC-004).
- [x] T014 [US3] Implement tray Show/Exit, close-while-busy hiding, idle-close exit, safe shutdown and account-action gating in src/TelegramDownloader.App/MainForm.cs (FR-005, FR-012; SC-004).

## Phase 6: Polish & Cross-Cutting Concerns

- [x] T015 Update README.md with v2 folder semantics, conservative naming examples, collision/replacement policy, background lifecycle and cancellation limits (FR-006–FR-013).
- [x] T016 Run Release tests/build, locked restore/publication, CLI/GUI checks and convergence review; record measured results and unverified live cases in specs/002-parallel-downloads-naming/validation.md (FR-014; SC-005).

## Dependencies & Execution Order

T001 -> T002 -> parallel story work. T003 before T006; T004 before T005; T008 before T009/T010.
T005 includes T011 because they share a file. T007/T012/T014 share one UI owner and are sequential.
Queue integration requires T005/T010; UI can compile against the stable boundary before integration.
T013's tests precede shutdown implementation within T006. T016 follows all functional work.

## Parallel Examples

- US1: queue tests/scheduler and transport tests/refactor run in separate files; UI observes
  the declared queue contract.
- US2: naming/filesystem implementation runs alongside transport; metadata shape is agreed in T002.
- US3: lifecycle tests run in the queue workstream while tray behavior is built by the UI owner.

## Implementation Strategy

Preserve the existing successful Telegram path while changing only lifecycle ownership,
metadata/naming and presentation. First establish deterministic tests, then integrate the queue
and folder flow, then validate the published EXE. Commit each Spec Kit phase.
No unattended downloads of private media are required for automated verification.

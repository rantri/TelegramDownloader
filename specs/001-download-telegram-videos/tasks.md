# Tasks: Download Telegram Videos

**Input**: Design documents in `specs/001-download-telegram-videos/`
**Prerequisites**: spec.md, plan.md, research.md, data-model.md, contracts/interfaces.md
**Tests**: Required by FR-014 and constitution V; use synthetic data and private temporary folders.
**Organization**: Shared setup, foundation, then independent presentation stories and publishing.

## Format: `[ID] [P?] [Story] Description`

[P] denotes work in separate files after stated prerequisites. Paths are repository-relative.

## Phase 1: Setup (Shared Infrastructure)

- [X] T001 Create global.json, Directory.Build.props, TelegramDownloader.slnx, .gitignore, and the three project files under src/ and tests/ with pinned .NET 11 dependencies (FR-010, FR-011).

## Phase 2: Foundational (Blocking Prerequisites)

- [X] T002 Define the shared gateway, errors, transfer/video records, and PostLink boundary in src/TelegramDownloader.Core/Contracts.cs and PostLink.cs per contracts/interfaces.md; VideoInfo "Size is greater than zero" (FR-003, FR-008).

**Checkpoint**: Stable contracts enable core, account adapter, and presentation work in parallel.

## Phase 3: User Story 1 - Download from the CLI (Priority: P1)

**Goal**: Complete reliable link-to-file downloads with account setup.
**Independent Test**: Synthetic gateway tests plus CLI help/error smoke checks; real account cases documented separately.

### Tests for User Story 1

- [X] T003 [P] [US1] Write parser and download success/failure/cancellation/collision tests in tests/TelegramDownloader.Tests/PostLinkTests.cs and DownloadServiceTests.cs before core implementation (FR-001, FR-005, FR-006, FR-007, FR-014; SC-002, SC-003).
- [X] T004 [P] [US1] Write protected-storage, profile validation, lease, persistence failure, safe RPC mapping, synthetic login/session-reuse/cancellation/failed-login cleanup, and bounded reconnect/flood-wait policy tests in tests/TelegramDownloader.Tests/ProfileStoreTests.cs, TelegramErrorTests.cs and AuthenticationTests.cs before adapter implementation (FR-002, FR-008, FR-010, FR-013, FR-014; Constitution V).

### Implementation for User Story 1

- [X] T005 [US1] Implement exact public/private/topic parsing in src/TelegramDownloader.Core/PostLink.cs: "ChannelId is positive"; "MessageId is between 1 and Int32.MaxValue"; reject comment/invitation/malformed links (FR-001).
- [X] T006 [US1] Implement streamed temporary downloads, safe names, size verification, cancellation, atomic promotion and explicit overwrite in src/TelegramDownloader.Core/DownloadService.cs (FR-005, FR-006, FR-007).
- [X] T007 [P] [US1] Implement AppProfile.cs and ProfileStore.cs under src/TelegramDownloader.Core/ with DPAPI, restricted ACL, atomic save/reset and lease; "ApiId is positive"; "ApiHash is 32 hexadecimal characters"; "PhoneNumber is an international number beginning with +" (FR-002, FR-010, FR-013).
- [X] T008 [US1] Implement src/TelegramDownloader.Core/TelegramGateway.cs login, public/private exact-message resolution, video transfer including disabled-save attempts, cancellation, bounded reconnects, explicit persistence errors and safe failure mapping (FR-002, FR-003, FR-004, FR-008, FR-013).
- [X] T009 [P] [US1] Implement src/TelegramDownloader.App/Cli.cs commands, private prompts, help, progress, cancellation and documented exit codes (FR-002, FR-005, FR-008, FR-012; SC-001).

**Checkpoint**: Core tests pass; CLI provides independent user workflow.

## Phase 4: User Story 2 - Download through the GUI (Priority: P2)

**Goal**: Responsive window with identical account and download behavior.
**Independent Test**: Window launch and input/error interactions; manual live download checklist.

- [X] T010 [P] [US2] Implement src/TelegramDownloader.App/PromptDialog.cs masked/cancellable authentication and setup dialogs (FR-002, FR-009, FR-010).
- [X] T011 [US2] Implement src/TelegramDownloader.App/MainForm.cs link/destination browsing, account actions, progress, single-operation gating, cancellation and safe close (FR-009; SC-001, SC-003).
- [X] T012 [US2] Implement src/TelegramDownloader.App/Program.cs GUI/CLI routing and verify the window's launch/input states, recording evidence in specs/001-download-telegram-videos/validation.md (FR-009; SC-003).

## Phase 5: User Story 3 - Publish one executable (Priority: P3)

**Goal**: Copy one self-contained Windows executable and run either interface.
**Independent Test**: Publish, copy just EXE to empty directory, run help, invalid input and GUI.

- [X] T013 [P] [US3] Implement scripts/publish.ps1 and app project publishing properties for self-contained win-x64 with embedded native libraries and symbols (FR-011).
- [X] T014 [US3] Run isolated artifact CLI/GUI smoke checks and record exact output/size in specs/001-download-telegram-videos/validation.md (FR-011; SC-004).

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T015 [P] Write README.md covering setup, login, usage, original-file semantics, session storage/removal, Desktop restrictions, single-file extraction and dependency notices (FR-012).
- [X] T016 Run Release build/tests, review failure/privacy paths and quickstart scenarios, and record verified versus live-unverified results in specs/001-download-telegram-videos/validation.md (FR-014; SC-005).

## Dependencies & Execution Order

T001 -> T002 -> story work. T003 precedes T005/T006; T004 precedes T007/T008.
T007 precedes T008. T009 and T010 can be written against the stable core contract while the
adapter is developed; end-to-end integration waits for T006/T008. T010 precedes T011/T012.
T013 can run after setup; T014 waits for both interfaces. T016 waits for all implementation.

### Parallel Opportunities

- US1: core tests/download service and account storage/adapter are disjoint work streams;
  CLI presentation can use stable contracts.
- US2: prompts/form can be built against the completed boundary while Telegram work continues.
- US3: publish script and README can be prepared independently; smoke checks wait for the app.

## Implementation Strategy

Deliver the CLI core first, then integrate the GUI and publish the same binary.
Use focused tests before corresponding core/adapter changes. No live account is needed to
build, test, or publish. Manual Telegram verification remains explicitly unverified unless
the owner signs in; do not claim disabled-save retrieval works without that evidence.
Commit each completed Spec Kit phase. Analysis is read-only; convergence appends corrective
tasks only when actual gaps are found.

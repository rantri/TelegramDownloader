# Feature Specification: Parallel Downloads and Automatic Names

**Feature Branch**: `main`

**Created**: 2026-11-13

**Status**: Ready for planning

**Input**: Add parallel background downloads with visibility in the UI, and select only a
destination folder. Derive filenames from the original filename or post; use standard
scene-style episode names for TV series. Follow the full Spec Kit workflow.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Queue and monitor concurrent downloads (Priority: P1)

As the owner, I add post links while earlier downloads continue and see the state of each
download instead of waiting for one operation to finish.

**Why this priority**: Parallel work and continued access to the UI are the core new capability.

**Independent Test**: Queue five controlled videos; observe three active transfers and two
waiting, then finish/cancel individual items and verify queued items advance.

**Acceptance Scenarios**:

1. **Given** an accessible account and destination folder, **When** I add several links,
   **Then** up to three jobs run concurrently and later jobs wait in insertion order.
2. **Given** running jobs, **When** I add another link, **Then** input controls remain usable
   and the queue shows each item's link, generated filename when known, state, progress,
   destination, and result or error.
3. **Given** several jobs, **When** I cancel one waiting or running job, **Then** only that job
   is cancelled; other jobs continue. A running cancellation is shown immediately and waits
   for any in-flight network request to return before cleanup.
4. **Given** a failed or cancelled job, **When** I retry it, **Then** a fresh attempt is queued
   with the same link and folder, and other jobs remain unaffected.
5. **Given** an unavailable post or failed transfer, **When** it fails,
   **Then** its error stays visible and the queue continues processing other jobs.

---

### User Story 2 - Choose a folder and receive useful filenames (Priority: P1)

As the owner, I choose the destination folder once and add links without entering filenames.
TV episodes receive predictable names from the information already attached to the video.

**Why this priority**: Folder-only input makes repeated downloads practical and keeps episodes organized.

**Independent Test**: Supply synthetic filenames/captions and compare generated filenames
against the examples below; download two different files with colliding names into one folder.

**Acceptance Scenarios**:

1. **Given** a valid existing folder, **When** I download using the GUI or CLI,
   **Then** the tool chooses the filename and reports the actual completed path.
2. **Given** `ornek_S01E11.mp4`, **When** naming is applied,
   **Then** the output is `ornek.S01E11.mp4`.
3. **Given** `video_42.mp4` and post text `דוגמה עונה 1 פרק 11`,
   **When** naming is applied, **Then** the output is `דוגמה.S01E11.mp4`.
4. **Given** an original name containing a clear show title and episode identifier,
   **When** the post text disagrees, **Then** original-name episode information wins;
   information from conflicting identities is not combined.
5. **Given** insufficient episode information, **When** naming is applied,
   **Then** the tool uses a safe original name, useful post title, or deterministic fallback
   without inventing a show name, season, episode, quality, codec, or release group.
6. **Given** an existing file or a name used by another active download,
   **When** a new job chooses the same name, **Then** a numbered suffix preserves both files.
   Explicit replacement may replace an existing file only after success, and never an active job's target.

---

### User Story 3 - Leave downloads running in the background (Priority: P2)

As the owner, I minimize or close the main window while downloads continue, then return
from the tray to inspect results. I can explicitly exit when I want work to stop.

**Why this priority**: Background operation must have clear ownership and predictable shutdown.

**Independent Test**: Start controlled jobs, hide and restore the window, then explicitly exit
and verify jobs stop and incomplete output is not promoted.

**Acceptance Scenarios**:

1. **Given** pending jobs, **When** I minimize or close the main window,
   **Then** work continues and a tray icon provides Show and Exit actions.
2. **Given** a hidden window, **When** I open it from the tray,
   **Then** the same queue and current progress/results are visible.
3. **Given** active or waiting jobs, **When** I explicitly exit,
   **Then** new work stops, jobs are cancelled, resources are released, and unfinished files
   are cleaned up or remain clearly marked partial.
4. **Given** idle application state, **When** I close the window,
   **Then** the application exits. A restart begins with an empty queue.

---

### Edge Cases

- Invalid links, nonexistent folders, and file paths supplied as folders fail before queueing.
- Cancelled waiting jobs never start; cancelling one transfer never disconnects unrelated jobs.
- Login is shared during a batch; simultaneous jobs do not display competing sign-in prompts.
- Account reconfiguration/removal is unavailable while the queue owns the account.
- Identical links can be queued deliberately; each job has its own state and safe output path.
- Filenames may contain Hebrew, reserved device names, path separators, control characters,
  invalid punctuation, multiple episode markers, very long titles, or misleading extensions.
- A caption containing only an episode number does not imply season one.
- Network refusals and service waits remain errors; jobs are not retried automatically.
- An external process may create a candidate filename during transfer; final publication
  must still preserve that file unless replacement was explicitly requested.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Provide an in-memory queue accepting additional links during existing downloads,
  with a default limit of three concurrent jobs and insertion-order scheduling of waiting jobs.
- **FR-002**: Display each job's link, filename when resolved, destination folder, state,
  transferred/total bytes or percentage, completed path, and actionable error.
- **FR-003**: Allow cancellation of one waiting or active job without cancelling others;
  display Cancelling while a running job finishes its in-flight request and cleans up.
- **FR-004**: Allow explicit retry of failed/cancelled jobs as a fresh queue attempt; no automatic retry.
- **FR-005**: Keep downloads running while the window is minimized or hidden with jobs pending,
  offer tray Show/Exit, and cancel/drain work on explicit Exit.
- **FR-006**: Accept only an existing destination folder in both interfaces. Retain the selected
  folder while adding more links during the current GUI session; reject exact-file destinations.
- **FR-007**: Derive names in this order: meaningful original episode name; explicit episode
  information in the post; useful original filename; useful post title; `video-<messageId>`.
  Names and extensions must come only from the chosen source and actual media information.
- **FR-008**: Recognize explicit `S01E02`, `S1E2`, `1x02`, English season/episode labels,
  and Hebrew season/episode labels. Normalize clear episodes to dot-separated title plus
  uppercase `SxxEyy` with at least two digits; preserve explicit multiple-episode identities.
- **FR-009**: Retain unambiguous release/episode suffixes present in a recognized original
  episode filename. Do not invent metadata or consult external title catalogs. When identifiers
  are ambiguous or incomplete, keep a safe original name instead of guessing.
- **FR-010**: Preserve the actual media extension, using a known media-type extension only when
  needed; sanitize Windows-invalid names, preserve Unicode, and keep files inside the chosen folder.
- **FR-011**: Reserve active output paths without case-sensitive duplicates, suffix collisions
  before the extension, and preserve external-race protection at final publication. Keep explicit
  replacement opt-in and never replace another active job's target.
- **FR-012**: Keep authentication and saved-session ownership safe across concurrent jobs and
  other application processes. Release account ownership when the batch is idle or the app exits.
- **FR-013**: Preserve transfer-integrity checks, bounded reconnect behavior, privacy, safe errors,
  and single-executable deployment. Do not persist/log full post text or credentials in queue history.
- **FR-014**: Provide automated, account-free tests for true overlap, concurrency limits,
  queue ordering, cancellation isolation, retry, shutdown, naming examples/ambiguities,
  collision races, and account/session coordination. Record live checks separately.

### Key Entities *(include if feature involves data)*

- **Queued download**: Unique job identifier, link, folder, replacement choice, state,
  progress, generated name, completed path, and safe error.
- **Video naming information**: Original filename, transient post text, media type,
  recognized episode identity, selected naming source, and safe proposed filename.
- **Output reservation**: A unique in-flight path that prevents simultaneous jobs from
  publishing over one another.
- **Batch account session**: Shared signed-in account ownership for pending/running work;
  independent per-job cancellation and explicit whole-batch shutdown.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Five controlled jobs demonstrate three overlapping active transfers and never
  more than three; waiting jobs begin in order as capacity becomes available.
- **SC-002**: Adding/cancelling a job remains responsive during controlled transfers; the UI
  reflects the action within one second without cancelling unrelated jobs.
- **SC-003**: All documented naming examples and ambiguity/collision tests produce deterministic,
  valid paths inside the selected folder, preserving existing files by default.
- **SC-004**: Hiding/restoring the window preserves the queue; explicit Exit leaves no running
  writers and no falsely completed files in controlled tests.
- **SC-005**: The Release build, automated suite, CLI folder checks, GUI checks, and single-file
  publication pass; manual Telegram scenarios and any unverified cases are recorded separately.

## Assumptions

- One feature covers both queue/background operation and automatic naming because the queue
  needs to show and protect automatically generated output paths.
- This supersedes feature 001's single-operation UI and exact-filename destination semantics.
  CLI remains a single-download command; parallel scheduling is managed in the GUI.
- Background means the running desktop application, including its tray presence; there is no
  Windows service, scheduled task, persistent queue, auto-start, or restart/resume mechanism.
- Three simultaneous jobs is the default; increasing concurrency is not exposed in the UI.
- Scene-style means predictable dotted episode filenames, not certification of full scene
  release rules. Hebrew/Unicode is retained; unavailable tags are omitted.
- Original-name episode identity has priority over a contradictory caption. Full post text
  is used transiently for naming and never copied into diagnostics or persisted queue state.
- Explicit replacement remains available, but automatic collision suffixes are the default.

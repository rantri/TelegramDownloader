# Validation: Parallel Downloads and Automatic Names

Date: 2026-11-13. Windows x64, .NET SDK 11.0.100, application version 2.0.0.

## Build, tests, and publication

- `dotnet restore TelegramDownloader.slnx --locked-mode`: passed.
- `dotnet build TelegramDownloader.slnx -c Release --no-restore`: passed, zero warnings/errors.
- `dotnet test TelegramDownloader.slnx -c Release --no-build`: **159 passed**, zero failed/skipped.
- `./scripts/publish.ps1`: passed; exactly one self-contained executable, 124,478,163 bytes.
- Output: `artifacts/publish/win-x64/TelegramDownloader.exe`.
- SHA-256: `469B6DF486BB1CD782937A3431F9E45C04333D0D40AFBC21844E901C7F77E2BE`.
- Published `--version` reports 2.0.0; `--help` documents folder-only input and queue lifecycle.
- Exact filename and nonexistent-folder CLI inputs both fail with exit 5 before account access.
- `git diff --check`: passed.

## Automated behavior coverage

Controlled gateways demonstrate three overlapping transfers, the concurrency cap and waiting
order, additions during work, independent queued/running cancellation, a Cancelling job retaining
its slot until its RPC drains, ordinary failure isolation, fresh retry entries, idle cleanup,
enqueue during cleanup, and shutdown rejecting admission and draining writers. A concurrent
cancel/shutdown regression exercises the cancellation-source disposal race.

Transport tests cover one shared login, first-waiter cancellation isolation, overlapping
operations, media-client initialization policies, global shutdown and profile-lease release.
Authentication/session tests use isolated synthetic profiles; the owner's credentials are not
test inputs. Metadata captions remain transient and are omitted from VideoInfo diagnostics.

Naming tables include the documented Turkish/Hebrew examples, caption/original precedence,
season zero, explicit multi-episodes, conflicting/incomplete identities, extensionless ranges,
quality suffixes such as 1080p, and conservative fallback for unsupported comma/plus/ampersand
episode lists. Tests cover Unicode/Windows sanitization, MIME fallback, long names and containment.
Filesystem tests verify active/existing case-insensitive collisions, explicit replacement,
external publication races, actual-name progress, reservation release, exact byte counts and
failed/cancelled replacement preserving the original file.

## Native GUI observations

The published executable opened correctly with an empty queue. The folder picker selected
an existing Unicode destination. Closing the idle window exited the process. No account
setup, sign-in, or download was invoked in this production-window check.

The nonshipping `tests/TelegramDownloader.GuiSmoke` harness links the actual production form
and injects synthetic transfers plus an isolated temporary profile. Native UI interaction verified:

- Five seeded jobs show three Downloading and two Queued; account actions are disabled while busy.
- Add stays usable and retains the folder. One measured add-to-visible-row round trip was 439 ms.
- Queued cancellation was visible in 262 ms in one measured check; existing active jobs continued.
- Cancelling an active row advanced the next waiting job while peers kept downloading.
- Retry created a new row and retained the cancelled history entry.
- Minimize/restore and busy-close/Show preserved the queue and progress.
- Explicit Exit cancelled and drained all work.

The final captured harness run exited with code 0 and wrote `artifacts/gui-smoke-result.json`:
five jobs Cancelled, peak writers 3, active writers 0, partial files 0, falsely completed files 0,
one hidden transition and one restored transition. The harness Release build passed with zero
warnings/errors. Its additional helper window invokes the actual production tray menu handlers.

The automation tool cannot target Windows' notification area. Physical tray-icon clicking and
double-clicking remain manual checks; Show/Exit handlers were exercised through the helper.
An initial hidden harness run was terminated because that tool could not restore it; its three
synthetic partial files were cleaned explicitly. It is not counted as a successful shutdown run.
The final successful run above used the helper and captured process output.

## Review and remaining live checks

Independent review covered all 14 functional requirements, five success criteria, 15 acceptance
scenarios, design decisions and five constitution principles. Review findings in shutdown races,
caption precedence and ambiguous episode parsing were fixed with focused regression coverage.
The test harness is justified by the native lifecycle checks and is not part of the shipped EXE.

No live Telegram downloads were performed for feature 002. The owner's earlier successful
private-post download belongs to feature 001's validation. Live simultaneous throughput, actual
service waits, protected-content availability, and cancellation against a stalled live RPC remain
manual scenarios in quickstart.md. Synthetic results do not establish Telegram throughput.
Per-job cancellation waits for the current RPC; explicit Exit stops the whole shared client.

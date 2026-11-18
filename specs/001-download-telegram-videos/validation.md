# Validation Record

Date: 2026-11-13. Environment: this Windows x64 PC, .NET SDK 11.0.100.

## Verified

| Check | Result |
|---|---|
| Spec quality | 16/16 criteria satisfied; no critical product questions remain |
| Artifact analysis | All 14 functional requirements and 5 success criteria mapped to tasks |
| Release solution build | Passed; zero warnings and zero errors |
| Automated tests | 77 passed, zero failed, zero skipped; no live account required |
| Locked restore | Passed after publication, confirming stable build/publish package graph |
| Publishing | Self-contained win-x64, exactly one executable |
| Isolated executable | Copied EXE alone to artifacts/smoke; help/version run successfully |
| CLI failures | Unknown command and bad link: 2; missing parent: 5; redirected configure: 3 |
| GUI | Published window visually inspected: readable controls, no clipping at default size |
| GUI input feedback | Empty link and missing destination report useful errors without authentication |
| GUI lifecycle | Opens without arguments; idle Cancel disabled; window closes normally |
| Live sign-in | Owner confirmed successful sign-in |
| Live private-post download | Owner confirmed a completed transfer and local MP4 inspection |
| Unicode destination | Completed output present in a Unicode directory |
| Local MP4 structure | Valid top-level box boundaries through end of file; no leftover partial files |

Automated coverage includes public/private/topic link parsing (including the example's
64-bit chat ID), malformed/unsupported links, exact output bytes, safe filenames, default
collision rejection, replacement preservation, concurrent file creation, truncated media,
cancellation, zero-length media, profile encryption/ACLs/lease ownership, damaged profile,
session reuse, failed authentication cleanup, session-save failure/recovery, safe error
mapping, flood policy, sequential chunk ordering, pending-read cancellation, and local-write
failures. Tests use synthetic account values and media only.

## Published artifact

- Path: artifacts/publish/win-x64/TelegramDownloader.exe
- Version: 1.0.0
- Size: 124,433,107 bytes (approximately 118.7 MiB)
- SHA-256: E73ED82733E1EAA4D1EA64FCFD74E0FCAC84EFE9751F5FA65094E7263E00738E
- The publish directory contains only this EXE. Generated artifacts are ignored by Git.
- Native runtime components extract to the .NET cache at launch. Private account state is
  stored separately in the Windows user profile.

## Live validation follow-up

The owner confirmed a successful sign-in and private-post download to a Unicode directory.
A read-only container check found valid MP4 box boundaries through the end of the file.
The destination contained the completed video and no partial file.

## Remaining manual verification

Playback/decoding, byte-for-byte agreement with Telegram's source, public-post downloads,
protected/disabled-save status for this post, and live-network cancellation remain
unverified. The owner did not specify which interface performed the successful download,
so this result does not independently verify both interfaces' live transfer behavior.

The adapter attempts the account's available document download and reports any Telegram
refusal. Success with this private post does not establish unrestricted protected-content
access. Use quickstart.md for further manual scenarios and record outcomes without secrets.

Publication is self-contained and the isolated-copy smoke test passed on this PC.
A separate clean Windows machine without .NET installed was not available for an additional
deployment test. GUI progress/cancellation during a real transfer still awaits specific validation.

## Review resolutions

- Added explicit authentication lifecycle and retry-policy tests after artifact analysis.
- Preserved whitespace in passwords; GUI Setup can replace unreadable configuration.
- Sanitized filenames ending in spaces/dots; rejected duplicate leading path separators.
- Used sequential awaited chunks to avoid orphaned parallel writers after cancellation.
- Classified output-write failures as destination errors, surfaced shutdown persistence
  failures, and allowed recovery after a successful session save.
- Kept package lock files consistent between normal builds and single-file publication.

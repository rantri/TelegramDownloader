# Implementation Plan — First public release

**Branch** `codex/prepare-github-release` · **Date** 2026-11-18 · [Specification](spec.md)

## Summary

Publish a sanitized baseline on the empty GitHub remote. Build and test the existing Windows application on each incoming revision. Publish a verified ZIP and checksum when the maintainer tags a matching stable version.

## Technical Context

- Windows x64, PowerShell 7, C#, .NET SDK selected by `global.json` (11.0.100 feature band), WinForms, WTelegramClient 4.4.10, xUnit and NuGet lock files.
- GitHub-hosted Windows runners execute the same checks available to contributors. The desktop smoke harness compiles separately and can be exercised manually.
- A single project version feeds the CLI and published assembly. A release package includes the self-contained executable, the project license and upstream notices. Each release has a SHA-256 digest.
- Read-only verification handles pushes and PRs. Only a separate publication job receives repository write access. No Telegram account is needed. A batch push must identify each verified SHA even if an intermediate commit fails.
- No additional hosting, application services or changes to the Telegram session or download protocol.

## Constitution Check

| Principle | Before design | After design |
| --- | --- | --- |
| Personal-use simplicity | Retain the local application. | GitHub distribution uses the existing publish script. |
| .NET 11 | Use the pinned SDK. | Restore locked dependencies on Windows x64. |
| Credential and session privacy | Inspect examples, artifacts and ancestry. | Publish a clean root, use synthetic input and isolate release permissions. |
| Download integrity | Preserve destination and collision behavior. | Existing tests cover it. Check the distributed ZIP separately. |
| Proportionate verification | Test the solution without an account. | Include published help/version, negative tag checks, clean checkout, batch push, PR and first release. |

Both reviews pass. Live Telegram and interactive tray checks are manual and outside these packaging changes.

## Project Structure

| Location | Responsibility |
| --- | --- |
| `specs/003-prepare-github-release/` | Specification, quality checklist, research, data model, contract, quickstart, tasks and validation |
| `.github/workflows/` | Push/PR verification and tagged publication |
| `scripts/` | Local verification, existing publish entry point and release packaging |
| `src/TelegramDownloader.App/` | Version source and CLI reporting |
| `tests/`, `specs/001-*/`, `specs/002-*/` | Synthetic examples and privacy-safe validation history |
| `README.md`, `CONTRIBUTING.md`, `SECURITY.md`, `LICENSE`, `licenses/`, `.github/` | Acquisition, attribution, reports and contributor workflow |

## Delivery sequence

1. Review the publication boundary, select MIT and sanitize source material.
2. Implement the verified user package, contributor checks, and gated release.
3. Publish the clean root, exercise hosted checks with a real batch and PR, tag the verified revision and verify the public download.
4. Record local and hosted evidence, then assess convergence against the specification.

# Tasks — First public release

Paths are relative to the repository root. Each story has an independent verification step.

## Phase 1 — Setup

- [x] T001 Record the publication audit and public-history boundary in `specs/003-prepare-github-release/research.md`. Review existing GitHub refs and the owner-selected license.

## Phase 2 — Foundations

- [x] T002 Replace private examples across `README.md`, `specs/`, `src/` and `tests/`, verify ignore rules in `.gitignore`, and retain the original history only in a local ref.
- [x] T003 Add the selected MIT text to `LICENSE` and attribute the vendored Spec Kit material under `licenses/`.

## Phase 3 — US1, install the verified tool (P1, MVP)

**Independent test** Verify a package in a fresh directory and run its help and version commands.

- [x] T004 [P] [US1] Derive the CLI version in `src/TelegramDownloader.App/Cli.cs` from the project's existing version declaration.
- [x] T005 [P] [US1] Build a version-checked ZIP and SHA-256 manifest in `scripts/package.ps1`. Bundle the project license and the applicable upstream notices from `licenses/` and restored packages.
- [x] T006 [US1] Put the release download, verification, first launch, platform and account setup path in `README.md`.

## Phase 4 — US2, contribute a checked change (P2)

**Independent test** Run the local command from a clean checkout, inspect checks on a two-commit push and PR, and open each reporting entry point.

- [x] T007 [P] [US2] Implement account-free locked restore, Release build and test, GUI harness compilation, publish and executable smoke checks in `scripts/verify.ps1`.
- [x] T008 [US2] Configure read-only pushes and proposed PR merge checks in `.github/workflows/`. Identify each tested commit, including intermediate failures and new branches.
- [x] T009 [P] [US2] Document the fork, test and review workflow in `CONTRIBUTING.md`. Set up public issue and PR templates in `.github/` and a working private advisory route in `SECURITY.md`.

## Phase 5 — US3, publish a version (P3)

**Independent test** Exercise a mismatched tag locally, publish a matching tag and compare the downloaded package to its manifest.

- [x] T010 [US3] Gate tag publication in `.github/workflows/` on successful verification, matching version, complete staged files and preservation of public releases on retry.
- [x] T011 [P] [US3] Document version selection, tagging, notes, and recovery from an interrupted draft in `CONTRIBUTING.md`.

## Phase 6 — Polish and cross-cutting verification

- [ ] T012 Verify clean checkout, local tag rejection and asset contents against `specs/003-prepare-github-release/quickstart.md`.
- [ ] T013 Push the sanitized baseline, verify hosted batch and PR checks, and verify the tagged public download. Record evidence and limitations in `specs/003-prepare-github-release/validation.md`.

## Dependencies and execution order

Review the publication boundary before preparing the public snapshot. The package uses the project license and binary version. Verification and contributor documentation can be developed alongside packaging. The tagged release consumes checked assets after verification completes.

## Parallel examples and delivery

US1 can develop version reporting and archive assembly in parallel. US2 can write the contribution guide while implementing local verification. US3 can write release instructions while building the publishing job. Deliver the installation path first, run the local and GitHub contributor journeys next, then publish the matching tagged release.

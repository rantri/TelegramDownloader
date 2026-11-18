# Feature Specification — Prepare the first public release

**Feature branch** `codex/prepare-github-release`

**Created** 2026-11-18

**Status** Approved for planning

**Input** Publish TelegramDownloader on GitHub with checks for every commit, versioned releases, and support for users and contributors.

## User Scenarios & Testing

### User Story 1 — Run a verified download of the tool (Priority P1)

A Windows x64 user can find the current release, verify the package, unpack it and see the version before configuring a Telegram account. This is the shortest path from discovering the project to using it.

**Independent test** Follow the README from the public landing page on a clean Windows installation.

1. **Given** a new user, **when** they download and verify the release, **then** the published digest matches and the application reports the version on the release page.
2. **Given** a damaged package, **when** the user compares the digest, **then** they can recognize the mismatch before launching it.
3. **Given** the public repository and package, **when** the user inspects examples and notices, **then** they see synthetic account examples, the project license and the bundled components’ notices.

### User Story 2 — Contribute a checked change (Priority P2)

A contributor can reproduce the build, report an issue and submit a change for review. The maintainer can see which checks failed before accepting it.

**Independent test** Follow the contributor guide from a fresh clone and inspect the checks for a push and a pull request.

1. **Given** a branch push introducing multiple commits, **when** checks run, **then** each new commit has a traceable verification result, including a commit followed by a fix in the same push.
2. **Given** a pull request from a fork, **when** the proposed merge is tested, **then** verification needs no Telegram credentials or release privileges.
3. **Given** a security concern, **when** a contributor follows the reporting instructions, **then** they can contact maintainers privately.

### User Story 3 — Publish a version (Priority P3)

A maintainer can identify a tested revision by version and give users a stable download for it. This supports repeat releases after the initial publication.

**Independent test** Follow the release procedure for the first version and exercise a mismatched version locally.

1. **Given** a release tag matching the application's version, **when** all checks pass, **then** the versioned package, checksum and notes become available together.
2. **Given** a version mismatch, failed verification or already published version, **when** publication is attempted, **then** an invalid or replacement release cannot be published by that workflow.

### Edge Cases

- A first push to an empty repository, a newly created branch, and several commits pushed together.
- Fork contributions use synthetic tests. Telegram availability and access to a real post are independent of the checks.
- A maintainer retries publication after an interrupted upload. Users must only see a complete release.
- A public example or historical record could identify a private post, file or machine.

## Requirements

### Functional Requirements

- **FR-001** Users MUST be able to download, verify and run a self-contained Windows x64 release from the project landing page.
- **FR-002** The application, package and release tag MUST identify the same three-part version.
- **FR-003** Published source, history, examples and release contents MUST exclude personal account/session data, real private-post references and local download details.
- **FR-004** Each newly introduced commit on a pushed branch and each proposed pull-request merge MUST receive automated, account-free verification with the tested revision identifiable.
- **FR-005** Contributors MUST be able to run the documented checks from a clean checkout and report bugs, submit changes and report vulnerabilities privately.
- **FR-006** A maintainer MUST be able to publish a tagged version only after verification and version matching. Retrying MUST preserve an already published release.
- **FR-007** Public source and downloads MUST convey the owner-selected MIT license and applicable redistribution notices.

### Key Entities

- **Revision** — A source snapshot identified by its commit and associated verification result.
- **Release** — A version, its tagged revision, a Windows x64 package and its integrity digest.

## Success Criteria

### Measurable Outcomes

- **SC-001** One first-time user can reach the release from the landing page, compare a matching checksum and obtain the advertised version without building from source or logging in.
- **SC-002** All newly introduced commits in a two-commit push and the proposed merge in a pull request can be traced to completed verification.
- **SC-003** One tagged release contains a runnable package, checksum, notices and release notes. A mismatched tag fails validation before publication.
- **SC-004** A fresh checkout passes the documented contributor checks with zero Telegram credentials, and both the public issue and private reporting routes are reachable.

## Assumptions

- Windows x64 remains the supported platform. The initial public release uses the existing application version 2.0.0.
- GitHub hosts source, issues, checks and downloads. A maintainer selects each stable version. The account holder supplies credentials only during personal use.
- A new public history may begin from a sanitized snapshot. Earlier development history remains available locally.

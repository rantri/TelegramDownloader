# TelegramDownloader Constitution

## Core Principles

### I. Personal-Use Simplicity

The project MUST serve the owner's personal workflow for downloading Telegram videos.
Dependencies, abstractions, and infrastructure MUST address a concrete requirement in the
current specification or plan. Prefer the smallest design that satisfies that requirement;
speculative multi-user, hosting, and extensibility features MUST remain out of scope until
explicitly requested. This keeps a personal tool practical to maintain.

### II. .NET 11 by Default

Application code MUST use .NET 11 as the default platform. A different target is permitted
when a concrete compatibility constraint requires it; the plan MUST record the constraint
and chosen alternative. Setup documentation MUST identify the SDK version needed to build
the tool. This preserves the owner's platform preference without preventing necessary
compatibility decisions.

### III. Credential and Session Privacy

Telegram credentials, authentication codes, session material, and other secrets MUST stay
out of source control, logs, and test fixtures. Persisted credentials and sessions MUST use
storage restricted to the current user. Diagnostics MUST omit private message content and
authentication material. Automated tests MUST use synthetic data or isolated test doubles
instead of the owner's credentials.

### IV. Download Integrity

A download MUST be reported as successful only after its transfer completes and the output
is saved. Interrupted or failed transfers MUST remain distinguishable from completed files.
Existing files MUST NOT be silently overwritten; each download workflow MUST define its
collision behavior. Failures MUST identify the affected operation and provide an actionable
reason when available. Automatic retries, when implemented, MUST be bounded and honor
Telegram's reported wait requirements.

### V. Proportionate Verification

Code changes MUST pass the relevant build and existing automated checks before being
declared complete. Changes affecting download completion, file handling, authentication,
or retry behavior MUST include focused automated coverage for the affected success and
failure paths. Documentation-only changes require content validation, not application tests.
Live Telegram checks, when needed, MUST be explicit manual steps; routine automated checks
MUST run without a live account. Validation results and any unverified behavior MUST be
recorded with the change.

## Technology and Scope

- .NET 11 is the preferred application platform; exact SDK and dependency versions MUST
  be recorded during implementation for reproducible setup.
- The user interface, Telegram client library, supported operating systems, and persistence
  approach MUST be selected in feature specifications and plans according to actual needs.
  This constitution does not mandate a CLI, GUI, database, or hosted service.
- Download destinations and file naming behavior MUST be documented for each workflow.
  Application data and downloaded media MUST remain outside tracked source files.

## Development Workflow

Each feature MUST have a concise specification of its user-visible behavior and acceptance
criteria before implementation. Its plan MUST identify dependencies, relevant failure cases,
and any justified departure from a default in this constitution. Work MUST proceed in small,
reviewable changes with validation matched to the behavior affected.

The maintainer MUST check constitution compliance during planning and before accepting a
change. Self-review is sufficient for this personal project. Setup and usage documentation
MUST be updated when prerequisites, configuration, or user-visible behavior change.

## Governance

This constitution governs project specifications, plans, and implementation. The project
owner may amend it by recording the changed rule, its rationale, and any necessary follow-up
work. Amendments MUST update the version and last-amended date; the original ratification
date MUST remain unchanged.

Versions follow semantic versioning: MAJOR for incompatible principle removals or
redefinitions, MINOR for added principles or materially expanded guidance, and PATCH for
clarifications that do not change obligations. Initial adoption is version 1.0.0.

Every plan and completed change MUST be reviewed against the applicable principles.
Exceptions explicitly allowed by a principle MUST be justified in the plan. A change that
conflicts with a mandatory rule MUST be revised or accompanied by a constitution amendment.
No separate reviewer, committee, or release ceremony is required.

**Version**: 1.0.0 | **Ratified**: 2026-11-13 | **Last Amended**: 2026-11-13

# Research

Date: 2026-11-13. Decisions resolved without new user input or accessing account credentials.

## Shared transport

**Decision**: One gateway/client/lease per busy batch, serialized login/media setup, parallel
per-file operations, independent cooperative cancellation, global disposal on shutdown.
**Rationale**: WTelegram's pending-RPC machinery and own parallel transfer helpers support
concurrent requests. Its pinned Invoke API has no per-request cancellation token; disposing
the main client also disposes media clients. Therefore a cancelled job must not disconnect peers.
**Alternatives**: Per-job gateways conflict with the exclusive session lease; duplicated session
files increase risk/complexity; abandoning transfer tasks can leave writers behind.

Use a lifetime token for shared authentication rather than capturing the first job's token.
Track admitted operations so shutdown retains ownership until all are drained. Configure media
clients before connecting, because they do not inherit flood thresholds.
Sources: [pinned Client](https://github.com/wiz0u/WTelegramClient/blob/7c2baece8ab84e88899c5b04ef7d8ad19b4ef29b/src/Client.cs),
[pinned helpers](https://github.com/wiz0u/WTelegramClient/blob/7c2baece8ab84e88899c5b04ef7d8ad19b4ef29b/src/Client.Helpers.cs),
[pinned Session](https://github.com/wiz0u/WTelegramClient/blob/7c2baece8ab84e88899c5b04ef7d8ad19b4ef29b/src/Session.cs).

## Scene-style names

**Decision**: Deterministic dotted title + SxxEyy, preserving explicit multi-episode identity,
known original suffixes and media extension. Preserve Unicode for this user's Hebrew paths/titles.
**Rationale**: Episode tokens are recognized by media scanners. Full scene rules include
metadata, ASCII constraints and release procedures beyond information available here.
**Alternatives**: External catalogs or AI title guessing introduce dependencies and ambiguity;
invented season/quality/group information would produce misleading names.

Recognize S1E2, S01E02,1x02, English Season/Episode and Hebrew עונה/פרק labels with both
numbers explicit. Require a meaningful title. Prefer original identity over caption conflicts.
Unknown or ambiguous identities retain safe source names. Use known MIME extensions only
when the original lacks a usable video extension. Restrict/sanitize caption title candidates,
and keep complete captions transient.
Sources: [Kodi episode naming](https://kodi.wiki/view/Naming_video_files/Episodes),
[Plex TV naming](https://support.plex.tv/articles/naming-and-organizing-your-tv-show-files/),
[original scene rules mirror, section18](https://opentrackers.org/scenerules.org/html/2020_X265.html).

## Background lifecycle and paths

**Decision**: Tray-backed live process with an in-memory queue, automatic collision suffixes,
explicit replacement retained, and safe final publication.
**Rationale**: Satisfies background visibility without installing a service/database.
A process-wide reservation plus atomic non-overwriting move covers both sibling jobs and
external file creation. Do not assume filename uniqueness from post identity alone.
**Alternatives**: Persistent/resumable queues and configurable scheduling add unrequested scope.

# Data Model

## VideoInfo (extended)

Existing FileName, Size, Handle plus optional PostText and MimeType.
"PostText is transient and must not be logged or persisted."
Size remains positive for downloadable media. Handle stays adapter-private.

## Naming candidate

Original filename, selected caption line, explicit title/season/episode tokens, suffix,
extension, and safe output name. "Season and episode numbers must be explicit; never infer season one."
"Season zero is allowed for specials." At least two digits; preserve larger values.
"Unicode titles and media extensions must survive sanitization."
Only one identity source supplies episode numbers/title; no conflicting-source merge.

## DownloadJobSnapshot

Id Guid; Link string; DestinationFolder string; Overwrite bool; State enum; FileName string?;
BytesTransferred/TotalBytes long; OutputPath string?; Error string?.
States: Queued -> Resolving -> Downloading -> Completed, or Failed/Cancelled.
Active cancellation: Resolving/Downloading -> Cancelling -> Cancelled.
A cancelled queued item never enters Resolving. Retry creates a new Id/job with original inputs.

## Output reservation

Absolute selected path; owned until publish or failure cleanup completes.
"Active paths compare case-insensitively." Default collisions get ' (2)', ' (3)' etc before
the extension. Explicit overwrite may reuse an existing file, but not a reserved active path.

## Queue and batch

MaximumConcurrency defaults to3; tests may inject another positive limit.
"At most MaximumConcurrency jobs are admitted at once."
Runner owns shared gateway while busy, including cleanup. Queue/history/captions are not persisted.
Explicit exit rejects new jobs, cancels pending/running work and drains gateway/worker disposal.

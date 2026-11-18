# Research: Download Telegram Videos

Date: 2026-11-13. All design questions resolved without requiring private account access.

## Telegram integration

**Decision**: WTelegramClient 4.4.10 over Telegram MTProto with the owner's app credentials.
**Rationale**: Managed .NET package supports ordinary user accounts, private joined channels,
interactive verification, and streamed document downloads.
**Alternatives considered**: Bot API does not share the user's joined-chat access; Desktop
automation/cache extraction is brittle and unnecessary; native TDLib adds packaging complexity.

Sources: [package](https://www.nuget.org/packages/WTelegramClient/4.4.10),
[library FAQ](https://github.com/wiz0u/WTelegramClient/blob/master/FAQ.md),
[Telegram app setup](https://core.telegram.org/api/obtaining_api_id).

## Links and protected media

**Decision**: Parse exact public/private post links including topic paths; retrieve the exact
message. Private IDs are positive long values and require an access hash from joined chats.
Reject comment query links instead of accidentally retrieving their parent post.
**Rationale**: The sample ID exceeds signed 32-bit range. An album link still selects only
the referenced message in this product.
**Alternatives considered**: Scraping public web pages cannot retrieve private account media.

Telegram marks protected content with noforwards and instructs clients to prevent saving.
The library's low-level file helper does not itself inspect that flag. This product attempts
the account's available document download and reports server refusal. This is not evidence
that any particular protected video is downloadable; live outcomes remain unverified.

Sources: [link syntax](https://core.telegram.org/api/links#message-links),
[message flags](https://core.telegram.org/constructor/message),
[file errors](https://core.telegram.org/method/upload.getFile).

## Session privacy and cancellation

**Decision**: Use the client constructor accepting initial session bytes and a save callback.
Wrap persisted sessions/configuration in DPAPI CurrentUser and restrict directory ACLs.
**Rationale**: No plaintext credentials or Desktop session import; portable executable can
move while user-profile state stays private. Capture callback failures because the library
otherwise catches/logs persistence exceptions.
**Alternatives considered**: Plain session files depend on weaker key/file protection.

**Decision**: Disable automatic flood retry on main and media DC clients, bound reconnection,
dispose the client on cancellation, and await the actual operation before cleaning streams.
**Rationale**: WaitAsync(token) alone can abandon writers. Progress callbacks and token-aware
streams also check cancellation. Media clients do not inherit the main client's flood setting.

Sources: [client source](https://github.com/wiz0u/WTelegramClient/blob/master/src/Client.cs),
[session source](https://github.com/wiz0u/WTelegramClient/blob/master/src/Session.cs),
[download helper](https://github.com/wiz0u/WTelegramClient/blob/master/src/Client.Helpers.cs).

Implementation review found that the high-level helper starts parallel chunk operations;
an early failure can bypass its final wait for outstanding requests. The adapter therefore
uses sequential awaited getFile requests and writes (512 KiB chunks), favoring predictable
cancellation over maximum throughput. Synthetic tests cover ordering, truncated chunks,
cancellation while reading, and cancellation before writing. File references refresh once
only when document identity and expected size are unchanged.

## UI and publishing

**Decision**: Windows Forms and a single console executable, opening the GUI without arguments.
**Rationale**: Fits this PC, avoids a web server or separate GUI runtime, and preserves CLI exit behavior.
**Alternatives considered**: WPF is viable but unnecessary for a small form; cross-platform UI adds
scope the owner did not request.

**Decision**: .NET SDK 11.0.100, self-contained win-x64, PublishSingleFile and
IncludeNativeLibrariesForSelfExtract; no trimming.
**Rationale**: SDK/runtime availability verified locally. One output EXE; bundled native files
extract to the runtime cache on launch. DPAPI state remains separate by design.
[Microsoft single-file deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview).

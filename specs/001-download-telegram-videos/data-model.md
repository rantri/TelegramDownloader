# Data Model

## PostLink

Username (nullable string) or ChannelId (nullable long), exactly one populated; MessageId int.
Constraints: "ChannelId is positive"; "MessageId is between 1 and Int32.MaxValue".
HTTPS t.me/telegram.me public or /c links; optional topic path; reject comment links.
The final numeric path segment is the message. Query single/t is accepted without changing identity.

## AppProfile

ApiId int, ApiHash string, PhoneNumber string.
Constraints: "ApiId is positive"; "ApiHash is 32 hexadecimal characters";
"PhoneNumber is an international number beginning with +".
Codes and passwords live only in prompt memory. One encrypted profile/session per Windows user.

## VideoInfo and TransferProgress

VideoInfo contains FileName string, Size long, and opaque Handle object owned by the gateway.
Constraint: "Size is greater than zero". File names are untrusted and sanitized.
TransferProgress contains BytesTransferred long and TotalBytes long.

## Download request and result

Request: link string, destination string, overwrite bool.
Return completed absolute path; throw typed DownloadException or OperationCanceledException.
States: validating -> authenticating -> resolving -> transferring -> verifying -> complete.
Any active state may fail or cancel; only complete promotes the partial file to the destination.

## Storage lifecycle

Encrypted profile and session persist under LocalApplicationData/TelegramDownloader.
An exclusive lease prevents concurrent writers. Save atomically, verify persistence success.
Reset removes only this application's profile/session. Unique partial files reside beside
the output so final rename stays on one filesystem; failure cleans only its own partial.

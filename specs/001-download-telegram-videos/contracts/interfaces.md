# Interface Contracts

## Command line

```text
TelegramDownloader.exe                 # GUI
TelegramDownloader.exe gui
TelegramDownloader.exe --help
TelegramDownloader.exe configure       # interactive API ID/hash/phone; secret hash masked
TelegramDownloader.exe login           # interactive code/password as required
TelegramDownloader.exe download <post-link> <destination> [--overwrite]
TelegramDownloader.exe logout          # remove local saved credentials/session
```

Help/version do not touch account storage. Invalid arguments fail before authentication.
Success prints the completed path. Progress/errors go to stderr.
Exit codes: 0 success; 2 invalid input; 3 authentication/configuration; 4 inaccessible/
missing/non-video post; 5 destination or integrity error; 6 network/service wait;
7 account busy; 1 unexpected failure; 130 cancelled.
Noninteractive input must fail instead of hanging on account prompts.

## GUI

Window has post link, destination textbox + Save As browser, explicit Replace checkbox,
Download and Cancel buttons, progress/status, and account Setup, Sign in, Forget local account.
Prompts mask secret values. One operation at a time. Closing cancels safely.
Status includes completed absolute path or actionable failure, never raw RPC details.

## Shared C# boundary

Namespace TelegramDownloader.Core.

```csharp
public record PostLink(string? Username, long? ChannelId, int MessageId)
{
    public static PostLink Parse(string value);
}
public record VideoInfo(string FileName, long Size, object Handle);
public record TransferProgress(long BytesTransferred, long TotalBytes);
public interface ITelegramGateway : IAsyncDisposable
{
    Task LoginAsync(CancellationToken cancellationToken);
    Task<VideoInfo> ResolveVideoAsync(PostLink link, CancellationToken cancellationToken);
    Task DownloadAsync(VideoInfo video, Stream output,
        IProgress<TransferProgress>? progress, CancellationToken cancellationToken);
}
public sealed class DownloadService(ITelegramGateway gateway)
{
    public static string ValidateDestination(string destination, bool overwrite);
    public Task<string> DownloadAsync(string link, string destination, bool overwrite,
        IProgress<TransferProgress>? progress, CancellationToken cancellationToken);
}
public enum FailureKind
{
    InvalidInput, Authentication, NotFound, AccessDenied, Destination,
    Network, RateLimited, Busy, Unexpected
}
public sealed class DownloadException : Exception
{
    public DownloadException(FailureKind kind, string message, Exception? inner = null);
    public FailureKind Kind { get; }
}
```

ProfileStore exposes constructor(string? rootDirectory = null), RootDirectory, Load() returning
AppProfile?, Save(AppProfile), Reset(), AcquireLease() returning IDisposable, ReadSession(),
WriteSession(byte[]), and ThrowIfPersistenceFailed(). Save/Reset acquire their own leases;
gateway acquires one for its lifetime and calls session methods under that lease.
AppProfile(int ApiId, string ApiHash, string PhoneNumber) exposes Validate().
TelegramGateway(ProfileStore store, Func<string, string?> prompt) implements ITelegramGateway.
Only authentication challenge keys reach prompt; ordinary configuration returns library defaults.
Dispose releases both client and lease, including failure paths.

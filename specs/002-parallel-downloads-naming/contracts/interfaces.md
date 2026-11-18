# Interfaces and Behavior

## Shared records

```csharp
public sealed record VideoInfo(string FileName, long Size, object Handle,
    string? PostText = null, string? MimeType = null);
public sealed record TransferProgress(long BytesTransferred, long TotalBytes, string? FileName = null);
public static class VideoFileNamer
{
    public static string CreateFileName(VideoInfo video, int messageId);
}
```

ITelegramGateway retains its current method signatures. Implementations permit overlapping
ResolveVideoAsync/DownloadAsync calls after shared login. Cancelling one call must not dispose
the shared gateway. DisposeAsync is idempotent whole-owner shutdown and awaits active calls.

DownloadService retains constructor and DownloadAsync(link,destination,overwrite,progress,token).
ValidateDestination now accepts existing folders only. Its optional overwrite parameter stays
for source compatibility. Progress includes the actual allocated filename; return value is
the actual completed absolute path after any collision suffix.

## Queue

```csharp
public enum DownloadJobState
{
    Queued, Resolving, Downloading, Cancelling, Completed, Failed, Cancelled
}
public sealed record DownloadJobSnapshot(Guid Id, string Link, string DestinationFolder,
    bool Overwrite, DownloadJobState State, string? FileName, long BytesTransferred,
    long TotalBytes, string? OutputPath, string? Error);
public sealed class DownloadQueue : IAsyncDisposable
{
    public DownloadQueue(Func<ITelegramGateway> gatewayFactory, int maximumConcurrency = 3);
    public int MaximumConcurrency { get; }
    public bool IsBusy { get; }
    public string? SessionError { get; }
    public IReadOnlyList<DownloadJobSnapshot> Jobs { get; }
    public Guid Enqueue(string link, string destinationFolder, bool overwrite = false);
    public void Cancel(Guid id);
    public Guid Retry(Guid id);
    public Task WhenIdleAsync();
    public ValueTask DisposeAsync();
}
```

Snapshots are immutable and safe to read from the UI thread. UI polls every250ms;
network callbacks never access controls. Enqueue validates input synchronously.
Retry accepts only Failed/Cancelled and creates a new entry. Queue never owns/disposes the
ProfileStore itself; factory creates the gateway for each busy batch. SessionError contains
a safe cleanup error if normal idle disposal fails.

## GUI

Post link + destination folder + Browse folder + Replace checkbox + Add download.
Grid: filename/link, state, progress, bytes, destination/result.
Cancel selected / Retry selected; row error displayed in detail. Input stays enabled while
jobs run. Account actions require idle; explicit Exit stops jobs and closes.
Close while busy hides with tray notification; tray Show/double-click restores; closing idle exits.

## CLI v2

```text
TelegramDownloader.exe download <post-link> <existing-folder> [--overwrite]
```

Exact filename destinations now fail with exit5. Help documents this intentional change.
CLI remains foreground single-job with existing exit codes. Setup/login/logout unchanged.
Its Ctrl+C handler cancels the token and initiates owned gateway disposal to interrupt pending RPCs.
No changes to saved credentials/session format.

## Naming examples

| Original | Post | Expected |
|---|---|---|
| ornek_S01E11.mp4 | ignored | ornek.S01E11.mp4 |
| Example Show 1x2 1080p WEB-DL.mkv | ignored | Example.Show.S01E02.1080p.WEB-DL.mkv |
| video_42.mp4 | דוגמה עונה 1 פרק 11 | דוגמה.S01E11.mp4 |
| Show.S01E01E02.mkv | ignored | Show.S01E01E02.mkv |
| Show.S01E01-E03.mkv | ignored | Show.S01E01-E03.mkv |
| Show.S00E01.mp4 | ignored | Show.S00E01.mp4 |
| clip.mp4 | Episode11 | clip.mp4 |
| Show.S01E11.mp4 | Other Season2 Episode3 | Show.S01E11.mp4 |

Missing filename + post title uses safe first useful title line; missing both uses video-<id>.
A media-type extension is used only when needed; captions cannot change it.
Recognized video extensions are preserved. Missing/non-video suffixes use a known video MIME
mapping, otherwise .bin. Generic original stems video/file/document optionally followed by
separators/digits defer to a useful caption; other non-episode original names are retained.

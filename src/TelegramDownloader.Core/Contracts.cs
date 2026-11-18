namespace TelegramDownloader.Core;

public sealed record VideoInfo(string FileName, long Size, object Handle,
    string? PostText = null, string? MimeType = null)
{
    public override string ToString() => $"Video metadata ({Size} bytes)";
}
public sealed record TransferProgress(long BytesTransferred, long TotalBytes, string? FileName = null);

public interface ITelegramGateway : IAsyncDisposable
{
    Task LoginAsync(CancellationToken cancellationToken);
    Task<VideoInfo> ResolveVideoAsync(PostLink link, CancellationToken cancellationToken);
    Task DownloadAsync(VideoInfo video, Stream output,
        IProgress<TransferProgress>? progress, CancellationToken cancellationToken);
}

public enum FailureKind
{
    InvalidInput, Authentication, NotFound, AccessDenied, Destination,
    Network, RateLimited, Busy, Unexpected
}

public sealed class DownloadException(FailureKind kind, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public FailureKind Kind { get; } = kind;
}

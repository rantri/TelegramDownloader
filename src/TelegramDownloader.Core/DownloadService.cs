namespace TelegramDownloader.Core;

public sealed class DownloadService(ITelegramGateway gateway)
{
    private readonly ITelegramGateway _gateway = gateway;
    public async Task<string> DownloadAsync(string link, string destination, bool overwrite,
        IProgress<TransferProgress>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var post = PostLink.Parse(link);
        string? partial = null;
        OutputPathReservation? reservation = null;
        try
        {
            var requested = ValidateDestination(destination, overwrite);
            await _gateway.LoginAsync(cancellationToken).ConfigureAwait(false);
            var video = await _gateway.ResolveVideoAsync(post, cancellationToken).ConfigureAwait(false);
            if (video.Size <= 0)
                throw new DownloadException(FailureKind.NotFound, "This post does not contain a nonempty downloadable video.");
            reservation = OutputPathReservation.Acquire(requested, VideoFileNamer.CreateFileName(video, post.MessageId), overwrite);
            var namedProgress = new NamedProgress(progress, reservation);
            namedProgress.Report(new TransferProgress(0, video.Size));
            cancellationToken.ThrowIfCancellationRequested();
            partial = Path.Combine(requested, $".telegram-{Guid.NewGuid():N}.partial");
            await using (var stream = new FileStream(partial, FileMode.CreateNew, FileAccess.ReadWrite,
                FileShare.None, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await _gateway.DownloadAsync(video, stream, namedProgress, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (stream.Length != video.Size)
                    throw new DownloadException(FailureKind.Destination,
                        "The downloaded size does not match the video. The incomplete file was not saved; try again.");
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            // Same-directory rename publishes only a complete file. The non-overwrite path also
            // protects against another process creating the destination during the transfer.
            var output = reservation.Publish(partial);
            partial = null;
            namedProgress.Report(new TransferProgress(video.Size, video.Size));
            return output;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            throw new DownloadException(FailureKind.Destination,
                "Cannot save the video. Check the destination, permissions, free space, and whether the file already exists.",
                exception);
        }
        finally
        {
            if (partial is not null)
            {
                try { File.Delete(partial); }
                catch (IOException) { /* A locked leftover keeps its explicit .partial suffix. */ }
                catch (UnauthorizedAccessException) { }
            }
            reservation?.Dispose();
        }
    }

    public static string ValidateDestination(string destination, bool overwrite)
    {
        if (string.IsNullOrWhiteSpace(destination))
            throw new DownloadException(FailureKind.InvalidInput, "Choose an existing destination folder.");
        try
        {
            var path = Path.GetFullPath(destination);
            if (!Directory.Exists(path))
                throw new DownloadException(FailureKind.Destination, "Choose an existing destination folder; a file path cannot be used.");
            return path;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            throw new DownloadException(FailureKind.Destination,
                "Choose a valid existing destination folder.", exception);
        }
    }

    private sealed class NamedProgress(IProgress<TransferProgress>? target, OutputPathReservation reservation) : IProgress<TransferProgress>
    {
        public void Report(TransferProgress value) => target?.Report(value with { FileName = Path.GetFileName(reservation.Path) });
    }
}

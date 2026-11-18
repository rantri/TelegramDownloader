using System.Diagnostics;
using TelegramDownloader.Core;

namespace TelegramDownloader.Tests;

public sealed class DownloadServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "TelegramDownloader-tests", Guid.NewGuid().ToString("N"));
    private const string Link = "https://t.me/c/9000000001/42";
    private static readonly byte[] Data = Enumerable.Range(0, 4096).Select(i => (byte)(i % 251)).ToArray();

    public DownloadServiceTests() => Directory.CreateDirectory(_directory);
    public void Dispose() => Directory.Delete(_directory, true);

    [Fact]
    public async Task SavesExactBytesAndPassesExactPost()
    {
        var gateway = new FakeGateway(Data);
        var output = Path.Combine(_directory, "result.mp4");
        var path = await new DownloadService(gateway).DownloadAsync(Link, _directory, false, null, default);
        Assert.Equal(output, path);
        Assert.Equal(Data, await File.ReadAllBytesAsync(path));
        Assert.Equal(new PostLink(null, 9000000001L, 42), gateway.LastLink);
        Assert.Equal(1, gateway.Logins);
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task ExistingFileIsUntouchedAndRejectedBeforeLogin()
    {
        var output = Path.Combine(_directory, "result.mp4");
        await File.WriteAllTextAsync(output, "keep");
        var gateway = new FakeGateway(Data);
        var error = await Assert.ThrowsAsync<DownloadException>(() =>
            new DownloadService(gateway).DownloadAsync(Link, output, false, null, default));
        Assert.Equal(FailureKind.Destination, error.Kind);
        Assert.Equal("keep", await File.ReadAllTextAsync(output));
        Assert.Equal(0, gateway.Logins);
    }

    [Fact]
    public async Task ExplicitReplacementOnlyAfterSuccessfulTransfer()
    {
        var output = Path.Combine(_directory, "result.mp4");
        await File.WriteAllTextAsync(output, "keep");
        await new DownloadService(new FakeGateway(Data)).DownloadAsync(Link, _directory, true, null, default);
        Assert.Equal(Data, await File.ReadAllBytesAsync(output));
    }

    [Theory]
    [InlineData("short")]
    [InlineData("throw")]
    [InlineData("cancel")]
    public async Task FailedReplacementPreservesOldFileAndCleansPartial(string mode)
    {
        var output = Path.Combine(_directory, "result.mp4");
        await File.WriteAllTextAsync(output, "keep");
        var gateway = new FakeGateway(Data) { Mode = mode };
        await Assert.ThrowsAnyAsync<Exception>(() =>
            new DownloadService(gateway).DownloadAsync(Link, _directory, true, null, default));
        Assert.Equal("keep", await File.ReadAllTextAsync(output));
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task TruncatedDownloadHasNoCompletedOutput()
    {
        var output = Path.Combine(_directory, "new.mp4");
        var error = await Assert.ThrowsAsync<DownloadException>(() =>
            new DownloadService(new FakeGateway(Data) { Mode = "short" })
                .DownloadAsync(Link, _directory, false, null, default));
        Assert.Equal(FailureKind.Destination, error.Kind);
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Theory]
    [InlineData("../escape.mp4")]
    [InlineData("..\\escape.mp4")]
    [InlineData("C:\\outside.mp4")]
    [InlineData("CON.mp4")]
    [InlineData("..")]
    [InlineData("a:stream.mp4")]
    [InlineData("video .")]
    public async Task UntrustedFileNamesStayInChosenFolder(string name)
    {
        var gateway = new FakeGateway(Data) { FileName = name };
        var result = await new DownloadService(gateway).DownloadAsync(Link, _directory, false, null, default);
        Assert.Equal(_directory, Path.GetDirectoryName(result));
        Assert.Equal(Data, await File.ReadAllBytesAsync(result));
    }

    [Fact]
    public async Task FolderCollisionPreservesExisting()
    {
        await File.WriteAllTextAsync(Path.Combine(_directory, "result.mp4"), "keep");
        var path = await new DownloadService(new FakeGateway(Data)).DownloadAsync(Link, _directory, false, null, default);
        Assert.Equal(Path.Combine(_directory, "result (2).mp4"), path);
        Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(_directory, "result.mp4")));
        Assert.Equal(Data, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task ConcurrentFileCreationNeverOverwrittenByDefault()
    {
        var output = Path.Combine(_directory, "result.mp4");
        var gateway = new FakeGateway(Data) { OnDownload = () => File.WriteAllText(output, "other") };
        var path = await new DownloadService(gateway).DownloadAsync(Link, _directory, false, null, default);
        Assert.Equal("other", await File.ReadAllTextAsync(output));
        Assert.Equal(Path.Combine(_directory, "result (2).mp4"), path);
        Assert.Equal(Data, await File.ReadAllBytesAsync(path));
        Assert.Equal(2, Directory.GetFiles(_directory).Length);
    }

    [Fact]
    public async Task CancellationStopsBeforePublishingAndIsPrompt()
    {
        var output = Path.Combine(_directory, "result.mp4");
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gateway = new FakeGateway(Data) { Started = started, Mode = "wait" };
        var task = new DownloadService(gateway).DownloadAsync(Link, _directory, false, null, cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var clock = Stopwatch.StartNew();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2));
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task InvalidPathFailsBeforeAccountAccess()
    {
        var gateway = new FakeGateway(Data);
        await Assert.ThrowsAsync<DownloadException>(() =>
            new DownloadService(gateway).DownloadAsync(Link, Path.Combine(_directory, "missing", "file.mp4"), false, null, default));
        Assert.Equal(0, gateway.Logins);
    }

    [Fact]
    public async Task AuthenticationFailureNeverCreatesOutput()
    {
        var gateway = new FakeGateway(Data) { FailLogin = true };
        var error = await Assert.ThrowsAsync<DownloadException>(() =>
            new DownloadService(gateway).DownloadAsync(Link, _directory, false, null, default));
        Assert.Equal(FailureKind.Authentication, error.Kind);
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task RejectsEmptyMedia()
    {
        var error = await Assert.ThrowsAsync<DownloadException>(() =>
            new DownloadService(new FakeGateway([])).DownloadAsync(Link, _directory, false, null, default));
        Assert.Equal(FailureKind.NotFound, error.Kind);
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OverlappingJobsReserveCaseInsensitivePathsEvenWithReplacement(bool overwrite)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstGateway = new FakeGateway(Data) { Started = started, Gate = release.Task };
        var firstProgress = new CaptureProgress();
        var secondProgress = new CaptureProgress();
        var first = new DownloadService(firstGateway).DownloadAsync(Link, _directory, overwrite, firstProgress, default);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            var second = await new DownloadService(new FakeGateway(Data) { FileName = "RESULT.mp4" })
                .DownloadAsync(Link, _directory, overwrite, secondProgress, default);
            Assert.Equal("RESULT (2).mp4", Path.GetFileName(second));
            Assert.All(firstProgress.Values, value => Assert.Equal("result.mp4", value.FileName));
            Assert.All(secondProgress.Values, value => Assert.Equal("RESULT (2).mp4", value.FileName));
            Assert.Equal(Data, await File.ReadAllBytesAsync(second));
        }
        finally { release.TrySetResult(); }
        Assert.Equal(Path.Combine(_directory, "result.mp4"), await first);
        Assert.Equal(2, Directory.GetFiles(_directory).Length);
    }

    [Fact]
    public async Task OverlappingReplacementsThroughAJunctionKeepBothDownloads()
    {
        var target = Directory.CreateDirectory(Path.Combine(_directory, "videos")).FullName;
        var alias = Path.Combine(_directory, "alias");
        using var junction = Process.Start(new ProcessStartInfo("cmd.exe", "/d /c mklink /j alias videos")
        {
            WorkingDirectory = _directory, UseShellExecute = false, CreateNoWindow = true
        })!;
        await junction.WaitForExitAsync();
        Assert.Equal(0, junction.ExitCode);

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new DownloadService(new FakeGateway([1]) { Started = started, Gate = release.Task })
            .DownloadAsync(Link, target, true, null, default);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var second = await new DownloadService(new FakeGateway([2])).DownloadAsync(Link, alias, true, null, default);
            Assert.Equal("result (2).mp4", Path.GetFileName(second));
        }
        finally
        {
            release.TrySetResult();
            await first;
            Directory.Delete(alias);
        }
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(Path.Combine(target, "result.mp4")));
        Assert.Equal(new byte[] { 2 }, File.ReadAllBytes(Path.Combine(target, "result (2).mp4")));
        Assert.Equal(2, Directory.GetFiles(target).Length);
    }

    [Fact]
    public async Task AnExistingReservationFileIsPreserved()
    {
        var marker = Path.Combine(_directory, ".telegram-RESULT.MP4.lock");
        await File.WriteAllTextAsync(marker, "existing");
        var output = await new DownloadService(new FakeGateway(Data)).DownloadAsync(Link, _directory, true, null, default);
        Assert.Equal("result (2).mp4", Path.GetFileName(output));
        Assert.Equal("existing", await File.ReadAllTextAsync(marker));
        Assert.Equal(2, Directory.GetFiles(_directory).Length);
    }

    [Fact]
    public async Task FailedJobReleasesItsReservation()
    {
        await Assert.ThrowsAsync<DownloadException>(() => new DownloadService(new FakeGateway(Data) { Mode = "throw" })
            .DownloadAsync(Link, _directory, false, null, default));
        var path = await new DownloadService(new FakeGateway(Data)).DownloadAsync(Link, _directory, false, null, default);
        Assert.Equal(Path.Combine(_directory, "result.mp4"), path);
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task FinalProgressReportsRenamedPathAfterExternalCollision()
    {
        var progress = new CaptureProgress();
        var output = Path.Combine(_directory, "result.mp4");
        var gateway = new FakeGateway(Data) { OnDownload = () => File.WriteAllText(output, "other") };
        var path = await new DownloadService(gateway).DownloadAsync(Link, _directory, false, progress, default);
        Assert.Equal("result.mp4", progress.Values[0].FileName);
        Assert.Equal(Path.GetFileName(path), progress.Values[^1].FileName);
        Assert.Equal(Data.Length, progress.Values[^1].BytesTransferred);
        Assert.Equal("other", await File.ReadAllTextAsync(output));
    }

    [Fact]
    public async Task DirectoryCollisionUsesNextAvailableNumber()
    {
        Directory.CreateDirectory(Path.Combine(_directory, "result.mp4"));
        await File.WriteAllTextAsync(Path.Combine(_directory, "result (2).mp4"), "keep");
        var path = await new DownloadService(new FakeGateway(Data)).DownloadAsync(Link, _directory, true, null, default);
        Assert.Equal(Path.Combine(_directory, "result (3).mp4"), path);
        Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(_directory, "result (2).mp4")));
    }

    private sealed class CaptureProgress : IProgress<TransferProgress>
    {
        public List<TransferProgress> Values { get; } = [];
        public void Report(TransferProgress value) => Values.Add(value);
    }

    private sealed class FakeGateway(byte[] data) : ITelegramGateway
    {
        public string FileName { get; init; } = "result.mp4";
        public string Mode { get; init; } = "";
        public bool FailLogin { get; init; }
        public Action? OnDownload { get; init; }
        public TaskCompletionSource? Started { get; init; }
        public Task? Gate { get; init; }
        public int Logins { get; private set; }
        public PostLink? LastLink { get; private set; }
        public Task LoginAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Logins++;
            if (FailLogin) throw new DownloadException(FailureKind.Authentication, "Sign in again.");
            return Task.CompletedTask;
        }
        public Task<VideoInfo> ResolveVideoAsync(PostLink link, CancellationToken cancellationToken)
        {
            LastLink = link;
            return Task.FromResult(new VideoInfo(FileName, data.Length, this));
        }
        public async Task DownloadAsync(VideoInfo video, Stream output,
            IProgress<TransferProgress>? progress, CancellationToken cancellationToken)
        {
            Started?.TrySetResult();
            if (Gate is not null) await Gate.WaitAsync(cancellationToken);
            if (Mode == "wait") await Task.Delay(Timeout.Infinite, cancellationToken);
            await output.WriteAsync(data.AsMemory(0, Mode == "short" ? data.Length / 2 : data.Length), cancellationToken);
            OnDownload?.Invoke();
            if (Mode == "throw") throw new DownloadException(FailureKind.Network, "Connection interrupted.");
            if (Mode == "cancel") throw new OperationCanceledException();
            progress?.Report(new TransferProgress(output.Length, video.Size));
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

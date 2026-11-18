using TelegramDownloader.Core;
using TL;

namespace TelegramDownloader.Tests;

public sealed class AuthenticationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "TelegramDownloaderTests", Guid.NewGuid().ToString("N"));

    private ProfileStore Store()
    {
        var store = new ProfileStore(_root);
        store.Save(new(12345, new string('a', 32), "+15555550100"));
        return store;
    }

    [Fact]
    public async Task LoginReusesConnectionAndSavedSessionWithoutSavingChallengeValues()
    {
        var store = Store();
        using (store.AcquireLease()) store.WriteSession([9, 8, 7]);
        var fake = new FakeConnection();
        var factoryCount = 0;
        await using (var gateway = new TelegramGateway(store, key => key == "verification_code" ? "123456" : throw new Exception(),
            (config, session, save) =>
            {
                factoryCount++;
                Assert.Equal(new byte[] { 9, 8, 7 }, session);
                Assert.Equal("12345", config("api_id"));
                Assert.Equal("123456", config("verification_code"));
                Assert.Null(config("device_model"));
                save([4, 5, 6]);
                return fake;
            }))
        {
            await gateway.LoginAsync(default);
            await gateway.LoginAsync(default);
            Assert.Equal(1, factoryCount);
            Assert.Equal(1, fake.LoginCount);
            Assert.Equal(new byte[] { 4, 5, 6 }, store.ReadSession());
            Assert.Equal(FailureKind.Busy, Assert.Throws<DownloadException>(() => new ProfileStore(_root).AcquireLease()).Kind);
        }
        Assert.Equal(1, fake.DisposeCount);
        using var released = store.AcquireLease();
    }

    [Fact]
    public async Task FloodWaitDuringResumePreservesTheSavedSessionAndReportsTheWait()
    {
        var store = Store();
        using (store.AcquireLease()) store.WriteSession([1, 2, 3]);
        var fake = new FakeConnection { OnLogin = () => throw new RpcException(420, "FLOOD_WAIT_X", 21) };
        await using var gateway = new TelegramGateway(store, _ => throw new InvalidOperationException(), (_, _, _) => fake);
        var error = await Assert.ThrowsAsync<DownloadException>(() => gateway.LoginAsync(default));
        Assert.False(fake.ReloginOnFailedResume);
        Assert.Equal(FailureKind.RateLimited, error.Kind);
        Assert.Contains("21 seconds", error.Message);
        Assert.Equal(new byte[] { 1, 2, 3 }, store.ReadSession());
    }

    [Fact]
    public async Task DamagedTelegramSessionInsideValidLocalEncryptionReportsRecovery()
    {
        var store = Store();
        using (store.AcquireLease()) store.WriteSession([1, 2, 3]);
        await using var gateway = new TelegramGateway(store, _ => throw new InvalidOperationException());
        var error = await Assert.ThrowsAsync<DownloadException>(() => gateway.LoginAsync(default));
        Assert.Equal(FailureKind.Authentication, error.Kind);
        Assert.Contains("Forget local account", error.Message);
        Assert.Equal(new byte[] { 1, 2, 3 }, store.ReadSession());
    }

    [Fact]
    public async Task FailedLoginDisposesConnectionAndReleasesLease()
    {
        var store = Store();
        var fake = new FakeConnection { OnLogin = () => Task.FromException(new RpcException(400, "PHONE_CODE_INVALID")) };
        await using var gateway = new TelegramGateway(store, _ => null, (_, _, _) => fake);
        var error = await Assert.ThrowsAsync<DownloadException>(() => gateway.LoginAsync(default));
        Assert.Equal(FailureKind.Authentication, error.Kind);
        Assert.Equal(1, fake.DisposeCount);
        using var released = store.AcquireLease();
    }

    [Fact]
    public async Task CancellingLoginWaiterDoesNotCancelTheSharedLogin()
    {
        var store = Store();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fake = new FakeConnection
        {
            OnLogin = () => { started.SetResult(); return pending.Task; },
            OnDispose = () => pending.TrySetException(new ObjectDisposedException("synthetic connection"))
        };
        using var cancellation = new CancellationTokenSource();
        await using var gateway = new TelegramGateway(store, _ => null, (_, _, _) => fake);
        var login = gateway.LoginAsync(cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => login.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.False(pending.Task.IsCompleted);
        Assert.Equal(0, fake.DisposeCount);
        Assert.Equal(FailureKind.Busy, Assert.Throws<DownloadException>(() => store.AcquireLease()).Kind);
        pending.SetResult();
        await gateway.LoginAsync(default);
        Assert.Equal(1, fake.LoginCount);
    }

    [Fact]
    public async Task FailedSessionSavePreventsSuccessfulLoginReport()
    {
        var store = Store();
        var fake = new FakeConnection();
        await using var gateway = new TelegramGateway(store, _ => null, (_, _, save) =>
        {
            fake.OnLogin = () =>
            {
                Directory.CreateDirectory(Path.Combine(_root, "session.dpapi"));
                save([1, 2, 3]);
                return Task.CompletedTask;
            };
            return fake;
        });
        var error = await Assert.ThrowsAsync<DownloadException>(() => gateway.LoginAsync(default));
        Assert.Equal(FailureKind.Authentication, error.Kind);
        Assert.Equal(1, fake.DisposeCount);
    }

    [Fact]
    public async Task CancelledTransferDrainsTheUnderlyingWriterBeforeReturning()
    {
        var store = Store();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stop = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writerStopped = false;
        var fake = new FakeConnection
        {
            OnDownload = async () =>
            {
                started.SetResult();
                await stop.Task;
                writerStopped = true;
                throw new OperationCanceledException();
            },
            OnDispose = () => stop.TrySetResult()
        };
        await using var gateway = new TelegramGateway(store, _ => null, (_, _, _) => fake);
        await gateway.LoginAsync(default);
        using var output = new MemoryStream();
        using var cancellation = new CancellationTokenSource();
        var transfer = gateway.DownloadAsync(new("video.mp4", 10, new object()), output, null, cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();
        Assert.False(transfer.IsCompleted);
        Assert.Equal(0, fake.DisposeCount);
        stop.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => transfer.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.True(writerStopped);
        Assert.True(output.CanWrite);
    }

    [Fact]
    public async Task ChunkTransferPreservesOrderAndReportsExactCompletion()
    {
        var payload = Enumerable.Range(0, 600_000).Select(i => (byte)(i % 251)).ToArray();
        var offsets = new List<long>();
        using var stream = new MemoryStream();
        await TelegramGateway.TransferChunksAsync(payload.Length, stream, (offset, limit) =>
        {
            offsets.Add(offset);
            return Task.FromResult(payload.Skip((int)offset).Take(limit).ToArray());
        }, null, default);
        Assert.Equal(new long[] { 0, 512 * 1024 }, offsets);
        Assert.Equal(payload, stream.ToArray());
        Assert.True(stream.CanWrite);
    }

    [Fact]
    public async Task TruncatedChunkIsRejectedBeforeWriting()
    {
        using var stream = new MemoryStream();
        var error = await Assert.ThrowsAsync<DownloadException>(() => TelegramGateway.TransferChunksAsync(
            10, stream, (_, _) => Task.FromResult(new byte[4]), null, default));
        Assert.Equal(FailureKind.Destination, error.Kind);
        Assert.Equal(0, stream.Length);
    }

    [Fact]
    public async Task CancellationAfterPendingChunkReturnsDoesNotWriteOrRequestAnotherChunk()
    {
        using var stream = new MemoryStream();
        using var cancellation = new CancellationTokenSource();
        var reads = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => TelegramGateway.TransferChunksAsync(
            600_000, stream, (_, limit) =>
            {
                reads++;
                cancellation.Cancel();
                return Task.FromResult(new byte[limit]);
            }, null, cancellation.Token));
        Assert.Equal(1, reads);
        Assert.Equal(0, stream.Length);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OutputWriteFailuresAreDestinationErrors(bool denied)
    {
        using var stream = new FailingWriteStream(denied);
        var error = await Assert.ThrowsAsync<DownloadException>(() => TelegramGateway.TransferChunksAsync(
            4, stream, (_, _) => Task.FromResult(new byte[4]), null, default));
        Assert.Equal(FailureKind.Destination, error.Kind);
        Assert.Equal(0, stream.Length);
    }

    [Fact]
    public async Task NetworkReadFailureIsNotMisclassifiedAsDestinationFailure()
    {
        using var stream = new MemoryStream();
        var error = await Assert.ThrowsAsync<IOException>(() => TelegramGateway.TransferChunksAsync(
            4, stream, (_, _) => Task.FromException<byte[]>(new IOException("synthetic connection failure")), null, default));
        Assert.Equal(FailureKind.Network, TelegramGateway.TranslateException(error).Kind);
    }

    [Fact]
    public async Task SessionSaveFailureDuringCloseIsReportedAfterReleasingLease()
    {
        var store = Store();
        var fake = new FakeConnection
        {
            OnDispose = () =>
            {
                Directory.CreateDirectory(Path.Combine(_root, "session.dpapi"));
                store.WriteSession([1, 2, 3]);
            }
        };
        var gateway = new TelegramGateway(store, _ => null, (_, _, _) => fake);
        await gateway.LoginAsync(default);
        var error = await Assert.ThrowsAsync<DownloadException>(() => gateway.DisposeAsync().AsTask());
        Assert.Equal(FailureKind.Authentication, error.Kind);
        using var released = store.AcquireLease();
        await gateway.DisposeAsync();
        Assert.Equal(1, fake.DisposeCount);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private sealed class FakeConnection : ITelegramConnection
    {
        public Func<Task> OnLogin { get; set; } = () => Task.CompletedTask;
        public Action? OnDispose { get; init; }
        public Func<Task>? OnDownload { get; init; }
        public int LoginCount { get; private set; }
        public int DisposeCount { get; private set; }
        public bool? ReloginOnFailedResume { get; private set; }
        public Task LoginAsync(bool reloginOnFailedResume)
        {
            LoginCount++;
            ReloginOnFailedResume = reloginOnFailedResume;
            return OnLogin();
        }
        public Task<VideoInfo> ResolveVideoAsync(PostLink link, CancellationToken token) => throw new NotSupportedException();
        public Task DownloadAsync(VideoInfo video, Stream stream, IProgress<TransferProgress>? progress, CancellationToken token) =>
            OnDownload?.Invoke() ?? throw new NotSupportedException();
        public ValueTask DisposeAsync() { DisposeCount++; OnDispose?.Invoke(); return ValueTask.CompletedTask; }
    }

    private sealed class FailingWriteStream(bool denied) : MemoryStream
    {
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException(denied ? new UnauthorizedAccessException("synthetic denied write") : new IOException("synthetic full disk"));
    }
}

using TelegramDownloader.Core;

namespace TelegramDownloader.Tests;

public sealed class ParallelGatewayTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "TelegramDownloaderTests", Guid.NewGuid().ToString("N"));
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly VideoInfo Video = new("synthetic.mp4", 4, new object());

    private ProfileStore Store()
    {
        var store = new ProfileStore(_root);
        store.Save(new(12345, new string('a', 32), "+15555550100"));
        return store;
    }

    [Fact]
    public async Task ConcurrentLoginUsesOneConnectionAndFirstWaiterCannotPoisonConfiguration()
    {
        var entered = Signal();
        var release = Signal();
        var count = 0;
        var promptCount = 0;
        Func<string, string?>? configuration = null;
        var fake = new Connection
        {
            Login = async () =>
            {
                entered.SetResult();
                await release.Task;
                Assert.Equal("123456", configuration!("verification_code"));
            }
        };
        using var cancelled = new CancellationTokenSource();
        await using var gateway = new TelegramGateway(Store(), _ => { promptCount++; return "123456"; },
            (config, _, _) => { Interlocked.Increment(ref count); configuration = config; return fake; });
        var first = gateway.LoginAsync(cancelled.Token);
        await entered.Task.WaitAsync(Timeout);
        var second = gateway.LoginAsync(default);
        cancelled.Cancel();
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.WaitAsync(Timeout));
            Assert.False(second.IsCompleted);
            Assert.Equal(0, fake.DisposeCount);
        }
        finally { release.TrySetResult(); }
        await second.WaitAsync(Timeout);
        Assert.Equal(1, count);
        Assert.Equal(1, fake.LoginCount);
        Assert.Equal(1, promptCount);
    }

    [Fact]
    public async Task TransfersOverlapAndCancellingOneKeepsTheOtherAlive()
    {
        var entered = new[] { Signal(), Signal() };
        var reads = new[] { new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously),
            new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously) };
        var admitted = -1;
        var fake = new Connection
        {
            Download = (_, stream, progress, token) =>
            {
                var index = Interlocked.Increment(ref admitted);
                return TelegramGateway.TransferChunksAsync(4, stream, (_, _) =>
                {
                    entered[index].SetResult();
                    return reads[index].Task;
                }, progress, token);
            }
        };
        var store = Store();
        await using var gateway = new TelegramGateway(store, _ => null, (_, _, _) => fake);
        await gateway.LoginAsync(default);
        using var cancelled = new CancellationTokenSource();
        using var firstOutput = new MemoryStream();
        using var secondOutput = new MemoryStream();
        var first = gateway.DownloadAsync(Video, firstOutput, null, cancelled.Token);
        var second = gateway.DownloadAsync(Video, secondOutput, null, default);
        try
        {
            await Task.WhenAll(entered.Select(item => item.Task)).WaitAsync(Timeout);
            cancelled.Cancel();
            Assert.False(first.IsCompleted);
            Assert.Equal(0, fake.DisposeCount);
            Assert.Equal(FailureKind.Busy, Assert.Throws<DownloadException>(() => store.AcquireLease()).Kind);
        }
        finally
        {
            cancelled.Cancel();
            reads[0].TrySetResult([1, 2, 3, 4]);
            reads[1].TrySetResult([5, 6, 7, 8]);
        }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.WaitAsync(Timeout));
        await second.WaitAsync(Timeout);
        Assert.Equal(0, firstOutput.Length);
        Assert.Equal(new byte[] { 5, 6, 7, 8 }, secondOutput.ToArray());
        Assert.Equal(0, fake.DisposeCount);
    }

    [Fact]
    public async Task AFailedJobDoesNotDisposeAnotherActiveOperation()
    {
        var entered = Signal();
        var release = Signal();
        var fake = new Connection
        {
            Resolve = _ => Task.FromException<VideoInfo>(new DownloadException(FailureKind.NotFound, "Synthetic missing post.")),
            Download = async (_, _, _, _) => { entered.SetResult(); await release.Task; }
        };
        await using var gateway = new TelegramGateway(Store(), _ => null, (_, _, _) => fake);
        await gateway.LoginAsync(default);
        using var output = new MemoryStream();
        var transfer = gateway.DownloadAsync(Video, output, null, default);
        await entered.Task.WaitAsync(Timeout);
        try
        {
            var error = await Assert.ThrowsAsync<DownloadException>(() =>
                gateway.ResolveVideoAsync(PostLink.Parse("https://t.me/example/1"), default).WaitAsync(Timeout));
            Assert.Equal(FailureKind.NotFound, error.Kind);
            Assert.Equal(0, fake.DisposeCount);
            Assert.False(transfer.IsCompleted);
        }
        finally { release.TrySetResult(); }
        await transfer.WaitAsync(Timeout);
    }

    [Fact]
    public async Task WholeOwnerShutdownInterruptsRequestsAndRetainsLeaseUntilWritersDrain()
    {
        var entered = Signal();
        var interrupted = Signal();
        var finishWriter = Signal();
        var fake = new Connection
        {
            Download = async (_, stream, _, _) =>
            {
                entered.SetResult();
                await interrupted.Task;
                await finishWriter.Task;
                await stream.WriteAsync(new byte[] { 9 });
                throw new ObjectDisposedException("synthetic connection");
            },
            Dispose = () => { interrupted.TrySetResult(); return ValueTask.CompletedTask; }
        };
        var store = Store();
        var gateway = new TelegramGateway(store, _ => null, (_, _, _) => fake);
        await gateway.LoginAsync(default);
        using var output = new MemoryStream();
        var transfer = gateway.DownloadAsync(Video, output, null, default);
        await entered.Task.WaitAsync(Timeout);
        var firstClose = gateway.DisposeAsync().AsTask();
        var secondClose = gateway.DisposeAsync().AsTask();
        try
        {
            await interrupted.Task.WaitAsync(Timeout);
            Assert.False(firstClose.IsCompleted);
            Assert.False(secondClose.IsCompleted);
            Assert.Equal(FailureKind.Busy, Assert.Throws<DownloadException>(() => store.AcquireLease()).Kind);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => gateway.LoginAsync(default).WaitAsync(Timeout));
        }
        finally { finishWriter.TrySetResult(); }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => transfer.WaitAsync(Timeout));
        await Task.WhenAll(firstClose, secondClose).WaitAsync(Timeout);
        Assert.Equal(1, fake.DisposeCount);
        Assert.Equal(1, output.Length);
        using var released = store.AcquireLease();
    }

    [Fact]
    public async Task WholeOwnerShutdownAlsoInterruptsSharedLogin()
    {
        var entered = Signal();
        var pending = Signal();
        var fake = new Connection
        {
            Login = () => { entered.SetResult(); return pending.Task; },
            Dispose = () => { pending.TrySetException(new ObjectDisposedException("synthetic connection")); return ValueTask.CompletedTask; }
        };
        var store = Store();
        var gateway = new TelegramGateway(store, _ => null, (_, _, _) => fake);
        var login = gateway.LoginAsync(default);
        await entered.Task.WaitAsync(Timeout);
        await gateway.DisposeAsync().AsTask().WaitAsync(Timeout);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => login.WaitAsync(Timeout));
        Assert.Equal(1, fake.DisposeCount);
        using var released = store.AcquireLease();
    }

    [Fact]
    public async Task MediaInitializationIsSharedAndAppliesPolicyBeforeConnecting()
    {
        var entered = Signal();
        var release = Signal();
        var media = new SyntheticMedia();
        var created = 0;
        var connected = 0;
        var cache = new TelegramGateway.MediaClientCache<SyntheticMedia>(
            _ => { Interlocked.Increment(ref created); return Task.FromResult(media); },
            client =>
            {
                client.FloodThreshold = TelegramGateway.AutomaticFloodRetryThreshold;
                client.ReconnectLimit = TelegramGateway.MaximumReconnects;
            },
            async _ =>
            {
                Interlocked.Increment(ref connected);
                Assert.Equal(-1, media.FloodThreshold);
                Assert.Equal(2, media.ReconnectLimit);
                entered.SetResult();
                await release.Task;
                return media;
            }, _ => false);
        using var cancelled = new CancellationTokenSource();
        var first = cache.GetAsync(4, cancelled.Token);
        await entered.Task.WaitAsync(Timeout);
        var second = cache.GetAsync(4, default);
        cancelled.Cancel();
        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);
        release.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.WaitAsync(Timeout));
        Assert.Same(media, await second.WaitAsync(Timeout));
        Assert.Equal(1, created);
        Assert.Equal(1, connected);
    }

    [Fact]
    public async Task MediaShutdownPreventsAConnectionFromStartingAfterCreation()
    {
        var creation = new TaskCompletionSource<SyntheticMedia>(TaskCreationOptions.RunContinuationsAsynchronously);
        var connectCount = 0;
        var cache = new TelegramGateway.MediaClientCache<SyntheticMedia>(
            _ => creation.Task,
            _ => { },
            _ => { connectCount++; return Task.FromResult(new SyntheticMedia()); }, _ => false);
        var pending = cache.GetAsync(4, default);
        cache.Close();
        creation.SetResult(new SyntheticMedia());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => pending.WaitAsync(Timeout));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => cache.GetAsync(5, default));
        Assert.Equal(0, connectCount);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FailedMediaClientIsReplacedAndLateFailureCannotEvictItsReplacement(bool disconnected)
    {
        var media = new SyntheticMedia();
        var cache = new TelegramGateway.MediaClientCache<SyntheticMedia>(
            _ => Task.FromResult(media), _ => { }, _ => Task.FromResult(media), client => client.Disconnected);
        var original = await cache.GetAsync(4, default);
        if (disconnected) original.Disconnected = true;
        else cache.Invalidate(4, original);
        media = new SyntheticMedia();
        var replacement = await cache.GetAsync(4, default);
        Assert.Same(media, replacement);
        cache.Invalidate(4, original);
        media = new SyntheticMedia();
        Assert.Same(replacement, await cache.GetAsync(4, default));
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class SyntheticMedia
    {
        public bool Disconnected { get; set; }
        public int FloodThreshold { get; set; } = 60;
        public int ReconnectLimit { get; set; }
    }

    private sealed class Connection : ITelegramConnection
    {
        public Func<Task> Login { get; init; } = () => Task.CompletedTask;
        public Func<PostLink, Task<VideoInfo>> Resolve { get; init; } = _ => Task.FromResult(Video);
        public Func<VideoInfo, Stream, IProgress<TransferProgress>?, CancellationToken, Task> Download { get; init; } =
            (_, _, _, _) => Task.CompletedTask;
        public Func<ValueTask> Dispose { get; init; } = () => ValueTask.CompletedTask;
        public int LoginCount;
        public int DisposeCount;
        public Task LoginAsync(bool reloginOnFailedResume) { Interlocked.Increment(ref LoginCount); return Login(); }
        public Task<VideoInfo> ResolveVideoAsync(PostLink link, CancellationToken token) => Resolve(link);
        public Task DownloadAsync(VideoInfo video, Stream stream, IProgress<TransferProgress>? progress, CancellationToken token) =>
            Download(video, stream, progress, token);
        public ValueTask DisposeAsync() { Interlocked.Increment(ref DisposeCount); return Dispose(); }
    }
}

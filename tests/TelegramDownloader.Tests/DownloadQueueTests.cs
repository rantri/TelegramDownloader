using System.Collections.Concurrent;
using TelegramDownloader.Core;

namespace TelegramDownloader.Tests;

public sealed class DownloadQueueTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "TelegramQueueTests", Guid.NewGuid().ToString("N"));
    public DownloadQueueTests() => Directory.CreateDirectory(_folder);
    public void Dispose() => Directory.Delete(_folder, true);
    private static string Link(int id) => $"https://t.me/c/9000000001/{id}";

    [Fact]
    public async Task FiveJobsOverlapThreeAtATimeAndKeepSnapshotHistory()
    {
        var gateway = new ControlledGateway();
        await using var queue = new DownloadQueue(() => gateway);
        var ids = Enumerable.Range(1, 5).Select(i => queue.Enqueue(Link(i), _folder)).ToArray();
        await Until(() => gateway.Active == 3);
        Assert.Equal(3, gateway.Peak);
        Assert.Equal(2, queue.Jobs.Count(x => x.State == DownloadJobState.Queued));
        gateway.Release(1);
        await Until(() => gateway.Started.ContainsKey(4));
        Assert.False(gateway.Started.ContainsKey(5));
        gateway.Release(2);
        await Until(() => gateway.Started.ContainsKey(5));
        foreach (var id in Enumerable.Range(3, 3)) gateway.Release(id);
        await queue.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(queue.IsBusy);
        Assert.Equal(3, gateway.Peak);
        Assert.Equal(5, queue.Jobs.Count);
        Assert.All(queue.Jobs, job =>
        {
            Assert.Equal(DownloadJobState.Completed, job.State);
            Assert.Equal(4, job.BytesTransferred);
            Assert.Equal(4, job.TotalBytes);
            Assert.NotNull(job.FileName);
            Assert.True(File.Exists(job.OutputPath));
        });
        Assert.Equal(ids, queue.Jobs.Select(x => x.Id));
        Assert.Equal(1, gateway.DisposeCount);
        var snapshot = Assert.IsType<DownloadJobSnapshot[]>(queue.Jobs);
        snapshot[0] = snapshot[0] with { State = DownloadJobState.Failed };
        Assert.Equal(DownloadJobState.Completed, queue.Jobs[0].State);
    }

    [Fact]
    public async Task CancellingQueuedItemSkipsItAndKeepsInsertionOrder()
    {
        var gateway = new ControlledGateway();
        await using var queue = new DownloadQueue(() => gateway, 1);
        queue.Enqueue(Link(1), _folder);
        await Until(() => gateway.Started.ContainsKey(1));
        var waiting = queue.Enqueue(Link(2), _folder);
        queue.Enqueue(Link(3), _folder);
        queue.Cancel(waiting);
        Assert.Equal(DownloadJobState.Cancelled, queue.Jobs.Single(x => x.Id == waiting).State);
        gateway.Release(1);
        await Until(() => gateway.Started.ContainsKey(3));
        Assert.False(gateway.Started.ContainsKey(2));
        gateway.Release(3);
        await queue.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new[] { 1, 3 }, gateway.StartOrder.ToArray());
    }

    [Fact]
    public async Task CancellingActiveItemDoesNotDisposeOrCancelItsPeer()
    {
        var gateway = new ControlledGateway();
        await using var queue = new DownloadQueue(() => gateway, 2);
        var first = queue.Enqueue(Link(1), _folder);
        var second = queue.Enqueue(Link(2), _folder);
        await Until(() => gateway.Active == 2);
        queue.Cancel(first);
        await Until(() => queue.Jobs.Single(x => x.Id == first).State == DownloadJobState.Cancelled);
        Assert.Equal(0, gateway.DisposeCount);
        Assert.Equal(DownloadJobState.Downloading, queue.Jobs.Single(x => x.Id == second).State);
        gateway.Release(2);
        await queue.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(DownloadJobState.Completed, queue.Jobs.Single(x => x.Id == second).State);
        Assert.Single(Directory.GetFiles(_folder));
    }

    [Fact]
    public async Task PendingReadKeepsCancellingSlotUntilItDrainsWhilePeerFinishes()
    {
        var gateway = new ControlledGateway { IgnoreJobCancellationDuringRead = true };
        await using var queue = new DownloadQueue(() => gateway, 2);
        var cancelled = queue.Enqueue(Link(1), _folder);
        var peer = queue.Enqueue(Link(2), _folder);
        queue.Enqueue(Link(3), _folder);
        await Until(() => gateway.Active == 2);
        queue.Cancel(cancelled);
        Assert.Equal(DownloadJobState.Cancelling, queue.Jobs.Single(x => x.Id == cancelled).State);
        Assert.False(gateway.Started.ContainsKey(3));
        Assert.Equal(0, gateway.DisposeCount);
        gateway.Release(2);
        await Until(() => gateway.Started.ContainsKey(3));
        Assert.Equal(DownloadJobState.Completed, queue.Jobs.Single(x => x.Id == peer).State);
        Assert.Equal(DownloadJobState.Cancelling, queue.Jobs.Single(x => x.Id == cancelled).State);
        gateway.Release(1);
        gateway.Release(3);
        await queue.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(DownloadJobState.Cancelled, queue.Jobs.Single(x => x.Id == cancelled).State);
        Assert.Equal(2, gateway.Peak);
        Assert.Equal(2, Directory.GetFiles(_folder).Length);
    }

    [Fact]
    public async Task ConcurrentCancellationAndShutdownAreSafeToRepeat()
    {
        for (var iteration = 0; iteration < 50; iteration++)
        {
            var gateway = new ControlledGateway { IgnoreJobCancellationDuringRead = true };
            var queue = new DownloadQueue(() => gateway);
            var id = queue.Enqueue(Link(1), _folder);
            await Until(() => gateway.Active == 1);
            await Task.WhenAll(Task.Run(() => queue.Cancel(id)),
                Task.Run(async () => await queue.DisposeAsync())).WaitAsync(TimeSpan.FromSeconds(5));
            queue.Cancel(id);
            Assert.False(queue.IsBusy);
            Assert.Equal(0, gateway.Active);
        }
        Assert.Empty(Directory.GetFiles(_folder));
    }

    [Fact]
    public async Task RetryCreatesFreshAttemptAndANewBatchAfterIdle()
    {
        var created = 0;
        await using var queue = new DownloadQueue(() => new ControlledGateway
        {
            FailureId = Interlocked.Increment(ref created) == 1 ? 1 : null,
            AutoComplete = true
        });
        var original = queue.Enqueue(Link(1), _folder);
        await queue.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(DownloadJobState.Failed, queue.Jobs.Single().State);
        var retried = queue.Retry(original);
        Assert.NotEqual(original, retried);
        await queue.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, created);
        Assert.Equal(DownloadJobState.Failed, queue.Jobs.Single(x => x.Id == original).State);
        Assert.Equal(DownloadJobState.Completed, queue.Jobs.Single(x => x.Id == retried).State);
    }

    [Fact]
    public async Task OrdinaryFailureDoesNotStopUnrelatedWork()
    {
        var gateway = new ControlledGateway { FailureId = 1 };
        await using var queue = new DownloadQueue(() => gateway);
        var failed = queue.Enqueue(Link(1), _folder);
        var good = queue.Enqueue(Link(2), _folder);
        await Until(() => queue.Jobs.Single(x => x.Id == failed).State == DownloadJobState.Failed && gateway.Started.ContainsKey(2));
        Assert.Equal("Post not found.", queue.Jobs.Single(x => x.Id == failed).Error);
        Assert.Equal(0, gateway.DisposeCount);
        gateway.Release(2);
        await queue.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(DownloadJobState.Completed, queue.Jobs.Single(x => x.Id == good).State);
    }

    [Fact]
    public async Task EnqueueDuringIdleCleanupStartsAnotherBatchAfterDisposal()
    {
        var releaseDisposal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new ControlledGateway { AutoComplete = true, DisposalGate = releaseDisposal };
        var second = new ControlledGateway { AutoComplete = true };
        var created = 0;
        await using var queue = new DownloadQueue(() => Interlocked.Increment(ref created) == 1 ? first : second);
        queue.Enqueue(Link(1), _folder);
        await Until(() => first.DisposeCount == 1);
        Assert.True(queue.IsBusy);
        queue.Enqueue(Link(2), _folder);
        Assert.Equal(1, created);
        releaseDisposal.SetResult();
        await queue.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, created);
        Assert.All(queue.Jobs, x => Assert.Equal(DownloadJobState.Completed, x.State));
    }

    [Fact]
    public async Task ExplicitShutdownBreaksPendingReadsDrainsWritersAndRejectsNewWork()
    {
        var gateway = new ControlledGateway { IgnoreJobCancellationDuringRead = true };
        var queue = new DownloadQueue(() => gateway, 2);
        queue.Enqueue(Link(1), _folder);
        queue.Enqueue(Link(2), _folder);
        queue.Enqueue(Link(3), _folder);
        await Until(() => gateway.Active == 2);
        await queue.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await queue.DisposeAsync();
        Assert.Equal(1, gateway.DisposeCount);
        Assert.Equal(0, gateway.Active);
        Assert.False(queue.IsBusy);
        Assert.All(queue.Jobs, x => Assert.Equal(DownloadJobState.Cancelled, x.State));
        Assert.Empty(Directory.GetFiles(_folder));
        Assert.Throws<ObjectDisposedException>(() => queue.Enqueue(Link(4), _folder));
    }

    [Fact]
    public async Task InvalidInputNeverCreatesBatch()
    {
        var created = 0;
        await using var queue = new DownloadQueue(() => { created++; return new ControlledGateway(); });
        Assert.Throws<DownloadException>(() => queue.Enqueue("bad", _folder));
        Assert.Throws<DownloadException>(() => queue.Enqueue(Link(1), Path.Combine(_folder, "missing")));
        Assert.Equal(0, created);
        Assert.Empty(queue.Jobs);
        Assert.False(queue.IsBusy);
    }

    [Fact]
    public async Task SessionCleanupFailureIsVisibleAndNextBatchCanRecover()
    {
        var created = 0;
        await using var queue = new DownloadQueue(() => new ControlledGateway
        {
            AutoComplete = true,
            FailDisposal = Interlocked.Increment(ref created) == 1
        });
        queue.Enqueue(Link(1), _folder);
        await queue.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("Session could not be saved.", queue.SessionError);
        queue.Enqueue(Link(2), _folder);
        await queue.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Null(queue.SessionError);
        Assert.All(queue.Jobs, x => Assert.Equal(DownloadJobState.Completed, x.State));
    }

    private static async Task Until(Func<bool> condition)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition()) await Task.Delay(10, deadline.Token);
    }

    private sealed class ControlledGateway : ITelegramGateway
    {
        private readonly ConcurrentDictionary<int, TaskCompletionSource> _gates = new();
        private readonly object _countSync = new();
        private int _active;
        private int _peak;
        private int _disposed;
        public int Active { get { lock (_countSync) return _active; } }
        public int Peak { get { lock (_countSync) return _peak; } }
        public int DisposeCount => Volatile.Read(ref _disposed);
        public ConcurrentDictionary<int, bool> Started { get; } = new();
        public ConcurrentQueue<int> StartOrder { get; } = new();
        public int? FailureId { get; init; }
        public bool AutoComplete { get; init; }
        public bool IgnoreJobCancellationDuringRead { get; init; }
        public bool FailDisposal { get; init; }
        public TaskCompletionSource? DisposalGate { get; init; }
        public void Release(int id) => _gates.GetOrAdd(id, _ => new(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();
        public Task LoginAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.CompletedTask; }
        public Task<VideoInfo> ResolveVideoAsync(PostLink link, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (link.MessageId == FailureId) throw new DownloadException(FailureKind.NotFound, "Post not found.");
            return Task.FromResult(new VideoInfo($"sample-{link.MessageId}.mp4", 4, link.MessageId));
        }
        public async Task DownloadAsync(VideoInfo video, Stream output, IProgress<TransferProgress>? progress, CancellationToken token)
        {
            var id = (int)video.Handle;
            var gate = _gates.GetOrAdd(id, _ => new(TaskCreationOptions.RunContinuationsAsynchronously));
            lock (_countSync) { _active++; _peak = Math.Max(_peak, _active); }
            Started[id] = true;
            StartOrder.Enqueue(id);
            try
            {
                progress?.Report(new TransferProgress(0, 4, video.FileName));
                if (!AutoComplete)
                {
                    if (IgnoreJobCancellationDuringRead) await gate.Task;
                    else await gate.Task.WaitAsync(token);
                }
                token.ThrowIfCancellationRequested();
                await output.WriteAsync(new byte[] { 1, 2, 3, 4 }, token);
                progress?.Report(new TransferProgress(4, 4, video.FileName));
            }
            finally { lock (_countSync) _active--; }
        }
        public async ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposed);
            foreach (var gate in _gates.Values) gate.TrySetResult();
            if (DisposalGate is not null) await DisposalGate.Task;
            if (FailDisposal) throw new DownloadException(FailureKind.Authentication, "Session could not be saved.");
        }
    }
}

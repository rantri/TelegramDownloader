namespace TelegramDownloader.Core;

public enum DownloadJobState
{
    Queued, Resolving, Downloading, Cancelling, Completed, Failed, Cancelled
}

public sealed record DownloadJobSnapshot(Guid Id, string Link, string DestinationFolder,
    bool Overwrite, DownloadJobState State, string? FileName, long BytesTransferred,
    long TotalBytes, string? OutputPath, string? Error);

public sealed class DownloadQueue : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly Func<ITelegramGateway> _gatewayFactory;
    private readonly List<Job> _jobs = [];
    private readonly HashSet<Guid> _active = [];
    private readonly SemaphoreSlim _wake = new(0);
    private Task? _runner;
    private Task? _shutdown;
    private Batch? _batch;
    private bool _stopping;
    private string? _sessionError;

    public DownloadQueue(Func<ITelegramGateway> gatewayFactory, int maximumConcurrency = 3)
    {
        ArgumentNullException.ThrowIfNull(gatewayFactory);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumConcurrency, 1);
        _gatewayFactory = gatewayFactory;
        MaximumConcurrency = maximumConcurrency;
    }
    public int MaximumConcurrency { get; }
    public bool IsBusy { get { lock (_sync) return _runner is not null; } }
    public string? SessionError { get { lock (_sync) return _sessionError; } }
    public IReadOnlyList<DownloadJobSnapshot> Jobs
    {
        get { lock (_sync) return _jobs.Select(job => job.Snapshot).ToArray(); }
    }

    public Guid Enqueue(string link, string destinationFolder, bool overwrite = false)
    {
        lock (_sync) ObjectDisposedException.ThrowIf(_stopping, this);
        _ = PostLink.Parse(link);
        var folder = DownloadService.ValidateDestination(destinationFolder, overwrite);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_stopping, this);
            var job = new Job(new(Guid.NewGuid(), link.Trim(), folder, overwrite,
                DownloadJobState.Queued, null, 0, 0, null, null));
            _jobs.Add(job);
            _runner ??= Task.Run(RunAsync);
            _wake.Release();
            return job.Snapshot.Id;
        }
    }

    public void Cancel(Guid id)
    {
        CancellationTokenSource? cancellation = null;
        lock (_sync)
        {
            var job = Find(id);
            var state = job.Snapshot.State;
            if (state == DownloadJobState.Queued)
                job.Snapshot = job.Snapshot with { State = DownloadJobState.Cancelled };
            else if (state is DownloadJobState.Resolving or DownloadJobState.Downloading)
                job.Snapshot = job.Snapshot with { State = DownloadJobState.Cancelling };
            else return;
            cancellation = job.Cancellation;
        }
        try { cancellation.Cancel(); }
        catch (ObjectDisposedException)
        {
            // Shutdown may finish between selecting this job and requesting cancellation.
            // A disposed source belongs to an already drained queue.
        }
        _wake.Release();
    }

    public Guid Retry(Guid id)
    {
        DownloadJobSnapshot original;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_stopping, this);
            original = Find(id).Snapshot;
            if (original.State is not (DownloadJobState.Failed or DownloadJobState.Cancelled))
                throw new InvalidOperationException("Only failed or cancelled downloads can be retried.");
        }
        return Enqueue(original.Link, original.DestinationFolder, original.Overwrite);
    }

    public Task WhenIdleAsync()
    {
        lock (_sync) return _runner ?? Task.CompletedTask;
    }

    private Job Find(Guid id) => _jobs.FirstOrDefault(job => job.Snapshot.Id == id)
        ?? throw new ArgumentException("The download was not found.", nameof(id));

    private async Task RunAsync()
    {
        while (true)
        {
            Batch? closing = null;
            lock (_sync)
            {
                if (!_stopping)
                {
                    foreach (var job in _jobs.Where(job => job.Snapshot.State == DownloadJobState.Queued))
                    {
                        if (_active.Count >= MaximumConcurrency) break;
                        if (_batch is null)
                        {
                            try
                            {
                                _batch = new Batch(_gatewayFactory());
                                _sessionError = null;
                            }
                            catch (Exception exception)
                            {
                                job.Snapshot = job.Snapshot with
                                {
                                    State = DownloadJobState.Failed,
                                    Error = SafeError(exception)
                                };
                                continue;
                            }
                        }
                        job.Snapshot = job.Snapshot with { State = DownloadJobState.Resolving };
                        _active.Add(job.Snapshot.Id);
                        var gateway = _batch.Gateway;
                        _ = Task.Run(() => RunJobAsync(job, gateway));
                    }
                }
                if (_active.Count == 0)
                {
                    // Keep _runner non-null while disposing, so account actions cannot race
                    // cleanup and newly queued work is picked up by this runner afterwards.
                    closing = _batch;
                    _batch = null;
                    if (closing is null && !HasWaitingJobs())
                    {
                        _runner = null;
                        return;
                    }
                }
            }
            if (closing is not null)
            {
                try { await closing.DisposeAsync().ConfigureAwait(false); }
                catch (Exception exception)
                {
                    lock (_sync) _sessionError = SafeError(exception);
                }
                lock (_sync)
                {
                    if (!HasWaitingJobs())
                    {
                        _runner = null;
                        return;
                    }
                }
                continue;
            }
            await _wake.WaitAsync().ConfigureAwait(false);
        }
    }

    private bool HasWaitingJobs() => !_stopping &&
        _jobs.Any(job => job.Snapshot.State == DownloadJobState.Queued);

    private async Task RunJobAsync(Job job, ITelegramGateway gateway)
    {
        try
        {
            var input = job.Snapshot;
            var result = await new DownloadService(gateway).DownloadAsync(input.Link,
                input.DestinationFolder, input.Overwrite, new InlineProgress(value =>
                {
                    lock (_sync)
                    {
                        if (job.Snapshot.State is not (DownloadJobState.Resolving or DownloadJobState.Downloading))
                            return;
                        job.Snapshot = job.Snapshot with
                        {
                            State = DownloadJobState.Downloading,
                            FileName = value.FileName ?? job.Snapshot.FileName,
                            BytesTransferred = value.BytesTransferred,
                            TotalBytes = value.TotalBytes
                        };
                    }
                }), job.Cancellation.Token).ConfigureAwait(false);
            lock (_sync)
                job.Snapshot = job.Snapshot with
                {
                    State = DownloadJobState.Completed,
                    FileName = Path.GetFileName(result),
                    OutputPath = result,
                    BytesTransferred = job.Snapshot.TotalBytes
                };
        }
        catch (OperationCanceledException)
        {
            lock (_sync) job.Snapshot = job.Snapshot with { State = DownloadJobState.Cancelled };
        }
        catch (Exception exception)
        {
            lock (_sync)
                job.Snapshot = job.Snapshot with
                {
                    State = job.Cancellation.IsCancellationRequested ? DownloadJobState.Cancelled : DownloadJobState.Failed,
                    Error = job.Cancellation.IsCancellationRequested ? null : SafeError(exception)
                };
        }
        finally
        {
            lock (_sync) _active.Remove(job.Snapshot.Id);
            _wake.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            _stopping = true;
            return new ValueTask(_shutdown ??= Task.Run(ShutdownAsync));
        }
    }

    private async Task ShutdownAsync()
    {
        Job[] outstanding;
        Task runner;
        Batch? batch;
        lock (_sync)
        {
            outstanding = _jobs.Where(job => job.Snapshot.State is DownloadJobState.Queued or
                DownloadJobState.Resolving or DownloadJobState.Downloading or DownloadJobState.Cancelling).ToArray();
            foreach (var job in outstanding)
                job.Snapshot = job.Snapshot with
                {
                    State = job.Snapshot.State == DownloadJobState.Queued
                        ? DownloadJobState.Cancelled : DownloadJobState.Cancelling
                };
            runner = _runner ?? Task.CompletedTask;
            batch = _batch;
        }
        foreach (var job in outstanding) job.Cancellation.Cancel();
        _wake.Release();
        if (batch is not null)
        {
            try { await batch.DisposeAsync().ConfigureAwait(false); }
            catch (Exception exception) { lock (_sync) _sessionError = SafeError(exception); }
        }
        await runner.ConfigureAwait(false);
        // Tokens are retained with history until shutdown, so Cancel cannot race a worker
        // disposing its source immediately after the UI selected that running row.
        lock (_sync)
            foreach (var job in _jobs) job.Cancellation.Dispose();
    }

    private static string SafeError(Exception exception) => exception is DownloadException known
        ? known.Message : "The operation failed unexpectedly. Check the connection and destination, then retry.";

    private sealed class Job(DownloadJobSnapshot snapshot)
    {
        public DownloadJobSnapshot Snapshot { get; set; } = snapshot;
        public CancellationTokenSource Cancellation { get; } = new();
    }

    private sealed class InlineProgress(Action<TransferProgress> report) : IProgress<TransferProgress>
    {
        public void Report(TransferProgress value) => report(value);
    }

    private sealed class Batch(ITelegramGateway gateway)
    {
        private readonly object _sync = new();
        private Task? _disposal;
        public ITelegramGateway Gateway { get; } = gateway;
        public Task DisposeAsync()
        {
            lock (_sync) return _disposal ??= Gateway.DisposeAsync().AsTask();
        }
    }
}

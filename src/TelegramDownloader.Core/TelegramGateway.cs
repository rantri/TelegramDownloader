using System.Globalization;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using TL;

[assembly: InternalsVisibleTo("TelegramDownloader.Tests")]

namespace TelegramDownloader.Core;

// This small transport seam tests authentication ownership and cancellation without an account.
internal interface ITelegramConnection : IAsyncDisposable
{
    Task LoginAsync(bool reloginOnFailedResume);
    Task<VideoInfo> ResolveVideoAsync(PostLink link, CancellationToken token);
    Task DownloadAsync(VideoInfo video, Stream output, IProgress<TransferProgress>? progress, CancellationToken token);
}

public sealed class TelegramGateway : ITelegramGateway
{
    internal const int AutomaticFloodRetryThreshold = -1;
    internal const int MaximumReconnects = 2;
    private readonly ProfileStore _store;
    private readonly Func<string, string?> _prompt;
    private readonly Func<Func<string, string?>, byte[]?, Action<byte[]>, ITelegramConnection> _factory;
    private readonly object _lifecycleSync = new();
    private readonly CancellationTokenSource _lifetime = new();
    private IDisposable? _lease;
    private ITelegramConnection? _connection;
    private Task? _loginTask;
    private Task? _connectionShutdown;
    private Task? _disposeTask;
    private TaskCompletionSource? _operationsDrained;
    private int _activeOperations;
    private bool _loggedIn;
    private bool _stopping;

    public TelegramGateway(ProfileStore store, Func<string, string?> prompt)
        : this(store, prompt, (config, session, save) => new WTelegramConnection(config, session, save)) { }

    internal TelegramGateway(ProfileStore store, Func<string, string?> prompt,
        Func<Func<string, string?>, byte[]?, Action<byte[]>, ITelegramConnection> factory)
    {
        _store = store;
        _prompt = prompt;
        _factory = factory;
    }

    public async Task LoginAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Task login;
        lock (_lifecycleSync)
        {
            ObjectDisposedException.ThrowIf(_stopping, this);
            if (_loginTask is null)
            {
                AdmitOperation();
                // Shared initialization continues if one waiter cancels, and never captures
                // a job token in the authentication callback used by the shared client.
                _loginTask = Task.Run(InitializeAsync);
            }
            login = _loginTask;
        }
        await login.WaitAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private async Task InitializeAsync()
    {
        try
        {
            _lifetime.Token.ThrowIfCancellationRequested();
            var lease = _store.AcquireLease();
            lock (_lifecycleSync) _lease = lease;
            _lifetime.Token.ThrowIfCancellationRequested();
            var profile = _store.Load() ?? throw new DownloadException(FailureKind.Authentication,
                "Set up the account first with Configure or the account Setup button.");
            profile.Validate();
            var session = _store.ReadSession();
            ITelegramConnection connection;
            try
            {
                _lifetime.Token.ThrowIfCancellationRequested();
                connection = _factory(key => Configuration(key, profile, _lifetime.Token), session, _store.WriteSession);
                lock (_lifecycleSync) _connection = connection;
            }
            finally
            {
                // ActionStore consumes the array synchronously during construction.
                if (session is not null) CryptographicOperations.ZeroMemory(session);
            }
            _lifetime.Token.ThrowIfCancellationRequested();
            await connection.LoginAsync(reloginOnFailedResume: false).ConfigureAwait(false);
            _lifetime.Token.ThrowIfCancellationRequested();
            _store.ThrowIfPersistenceFailed();
            lock (_lifecycleSync)
            {
                if (_stopping) throw new OperationCanceledException("The account session is shutting down.", _lifetime.Token);
                _loggedIn = true;
            }
        }
        catch (Exception exception)
        {
            // No job has been admitted to the connection before initialization succeeds.
            await StopConnectionIgnoringErrorsAsync().ConfigureAwait(false);
            ReleaseLease();
            _lifetime.Token.ThrowIfCancellationRequested();
            if (exception is OperationCanceledException) throw;
            throw TranslateException(exception);
        }
        finally { CompleteOperation(); }
    }

    public Task<VideoInfo> ResolveVideoAsync(PostLink link, CancellationToken cancellationToken) =>
        ExecuteAsync((connection, token) => connection.ResolveVideoAsync(link, token), cancellationToken);

    public Task DownloadAsync(VideoInfo video, Stream output, IProgress<TransferProgress>? progress,
        CancellationToken cancellationToken) => ExecuteAsync(async (connection, token) =>
    {
        await connection.DownloadAsync(video, output, progress, token).ConfigureAwait(false);
        return true;
    }, cancellationToken);

    private string? Configuration(string key, AppProfile profile, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        switch (key)
        {
            case "api_id": return profile.ApiId.ToString(CultureInfo.InvariantCulture);
            case "api_hash": return profile.ApiHash;
            case "phone_number": return profile.PhoneNumber;
            case "verification_code":
            case "password":
            case "email":
            case "email_verification_code":
                var response = _prompt(key);
                token.ThrowIfCancellationRequested();
                return response ?? throw new OperationCanceledException("Sign-in was cancelled.");
            case "first_name":
            case "last_name":
                throw new DownloadException(FailureKind.Authentication,
                    "This tool signs in to existing accounts. Check your phone number or create the account in Telegram first.");
            default: return null;
        }
    }

    private async Task<T> ExecuteAsync<T>(Func<ITelegramConnection, CancellationToken, Task<T>> action, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ITelegramConnection connection;
        lock (_lifecycleSync)
        {
            ObjectDisposedException.ThrowIf(_stopping, this);
            if (!_loggedIn || _connection is null)
                throw new DownloadException(FailureKind.Authentication, "Sign in before downloading a video.");
            connection = _connection;
            AdmitOperation();
        }
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
            try
            {
                linked.Token.ThrowIfCancellationRequested();
                // A cancelled job drains its own RPC/writer. Disposing a shared transport
                // here would abort unrelated jobs and release ownership prematurely.
                var result = await action(connection, linked.Token).ConfigureAwait(false);
                linked.Token.ThrowIfCancellationRequested();
                _store.ThrowIfPersistenceFailed();
                return result;
            }
            catch (Exception exception)
            {
                linked.Token.ThrowIfCancellationRequested();
                if (exception is OperationCanceledException) throw;
                throw TranslateException(exception);
            }
        }
        finally { CompleteOperation(); }
    }

    // Caller holds _lifecycleSync and has checked admission is still open.
    private void AdmitOperation()
    {
        if (_activeOperations++ == 0)
            _operationsDrained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private void CompleteOperation()
    {
        lock (_lifecycleSync)
            if (--_activeOperations == 0) _operationsDrained!.TrySetResult();
    }

    private Task StopConnectionAsync()
    {
        lock (_lifecycleSync)
        {
            if (_connectionShutdown is not null) return _connectionShutdown;
            if (_connection is not { } connection) return Task.CompletedTask;
            return _connectionShutdown = Task.Run(async () => await connection.DisposeAsync().ConfigureAwait(false));
        }
    }

    private async Task StopConnectionIgnoringErrorsAsync()
    {
        try { await StopConnectionAsync().ConfigureAwait(false); }
        catch { /* Drain admitted work and release ownership even if transport close fails. */ }
    }

    private void ReleaseLease()
    {
        IDisposable? lease;
        lock (_lifecycleSync)
        {
            lease = _lease;
            _lease = null;
        }
        lease?.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        lock (_lifecycleSync)
        {
            if (_disposeTask is { IsCompleted: true }) return ValueTask.CompletedTask;
            _stopping = true;
            return new(_disposeTask ??= Task.Run(DisposeCoreAsync));
        }
    }

    private async Task DisposeCoreAsync()
    {
        try
        {
            _lifetime.Cancel();
            // Whole-owner shutdown breaks network waits, then waits for all underlying
            // operations/writers before releasing the cross-process session lease.
            await StopConnectionIgnoringErrorsAsync().ConfigureAwait(false);
            Task drained;
            lock (_lifecycleSync) drained = _operationsDrained?.Task ?? Task.CompletedTask;
            await drained.ConfigureAwait(false);
            // Initialization may have published its connection after the first stop check.
            await StopConnectionIgnoringErrorsAsync().ConfigureAwait(false);
            ReleaseLease();
            if (_loggedIn) _store.ThrowIfPersistenceFailed();
        }
        finally
        {
            ReleaseLease();
            _lifetime.Dispose();
        }
    }

    internal static async Task TransferChunksAsync(long expectedSize, Stream output,
        Func<long, int, Task<byte[]>> readChunk, IProgress<TransferProgress>? progress, CancellationToken token)
    {
        const int chunkSize = 512 * 1024;
        long offset = 0;
        progress?.Report(new(0, expectedSize));
        while (offset < expectedSize)
        {
            token.ThrowIfCancellationRequested();
            // One awaited request and one awaited write at a time. No background writer can
            // survive an error or cancellation and race the download service's partial-file cleanup.
            var chunk = await readChunk(offset, chunkSize).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (chunk.Length != Math.Min(chunkSize, expectedSize - offset))
                throw new DownloadException(FailureKind.Destination,
                    "The video size changed or Telegram returned an incomplete file. Start the download again.");
            try { await output.WriteAsync(chunk, token).ConfigureAwait(false); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new DownloadException(FailureKind.Destination,
                    "The video could not be written. Check destination permissions and free disk space, then try again.", ex);
            }
            offset += chunk.Length;
            progress?.Report(new(offset, expectedSize));
        }
    }

    internal static DownloadException TranslateException(Exception exception)
    {
        if (exception is DownloadException known) return known;
        if (exception is RpcException rpc)
        {
            if (rpc.Code == 420)
                return new(FailureKind.RateLimited, rpc.X >= 0
                    ? $"Telegram requested a wait of {rpc.X} seconds. Try again after that wait; no automatic retry was made."
                    : "Telegram temporarily limited this operation. Wait before trying again.", rpc);
            if (rpc.Code == 401 || rpc.Message.StartsWith("PHONE_", StringComparison.Ordinal) ||
                rpc.Message.StartsWith("EMAIL_", StringComparison.Ordinal) ||
                rpc.Message.StartsWith("PASSWORD_", StringComparison.Ordinal) ||
                rpc.Message.StartsWith("API_ID_", StringComparison.Ordinal) ||
                rpc.Message is "SESSION_PASSWORD_NEEDED" or "USER_DEACTIVATED" or "USER_DEACTIVATED_BAN")
                return new(FailureKind.Authentication,
                    "Telegram could not sign in. Check the account settings, verification code or password, and sign in again.", rpc);
            if (rpc.Code == 403 || rpc.Message is "CHANNEL_PRIVATE" or "CHANNEL_INVALID" or "CHAT_ID_INVALID" or "PEER_ID_INVALID")
                return new(FailureKind.AccessDenied,
                    "This account cannot access or download the post. Check group membership and the post in Telegram.", rpc);
            if (rpc.Message is "MSG_ID_INVALID" or "MESSAGE_ID_INVALID" or "USERNAME_INVALID" or "USERNAME_NOT_OCCUPIED")
                return new(FailureKind.NotFound, "The linked post was not found. Check the copied post link in Telegram.", rpc);
            if (rpc.Message.StartsWith("FILE_REFERENCE_", StringComparison.Ordinal))
                return new(FailureKind.Network,
                    "Telegram's file reference has expired or changed. Start the download again to retrieve a fresh reference.", rpc);
            return new(FailureKind.Network, "Telegram could not complete the request. Check connectivity and try again later.", rpc);
        }
        if (exception is SocketException or HttpRequestException or TimeoutException)
            return new(FailureKind.Network, "Could not connect to Telegram. Check your connection and try again.", exception);
        if (exception is IOException)
            return new(FailureKind.Network, "The transfer was interrupted. Check the connection, destination permissions and free space, then try again.", exception);
        if (exception is UnauthorizedAccessException)
            return new(FailureKind.Destination, "The video could not be written. Check destination permissions and try again.", exception);
        if (exception is WTelegram.WTException sessionError &&
            (sessionError.Message.StartsWith("Exception while reading session file:", StringComparison.Ordinal) ||
             sessionError.Message.EndsWith("mismatched user_id or phone_number", StringComparison.Ordinal)))
            return new(FailureKind.Authentication, $"The saved session could not be used with these account settings. {ProfileStore.SessionRecovery}", exception);
        if (exception is WTelegram.WTException)
            return new(FailureKind.Network, "Telegram could not complete this operation. Check connectivity or sign in again.", exception);
        return new(FailureKind.Unexpected, "The operation failed unexpectedly. Close the tool and try again.", exception);
    }

    private sealed class WTelegramConnection : ITelegramConnection
    {
        private readonly WTelegram.Client _client;
        private readonly MediaClientCache<WTelegram.Client> _mediaClients;
        private readonly PrivateChannelLookup _privateChannels;
        private sealed record VideoHandle(PostLink Link, Document Document);

        public WTelegramConnection(Func<string, string?> config, byte[]? session, Action<byte[]> save)
        {
            WTelegram.Helpers.Log = static (_, _) => { };
            _client = new WTelegram.Client(key => config(key)!, session!, save)
            {
                FloodRetryThreshold = AutomaticFloodRetryThreshold,
                MaxAutoReconnects = MaximumReconnects,
                MaxCodePwdAttempts = 3
            };
            _mediaClients = new(
                dcId => _client.GetClientForDC(-dcId, connect: false),
                mediaClient =>
                {
                    // Media clients do not inherit these policies from the main client.
                    mediaClient.FloodRetryThreshold = AutomaticFloodRetryThreshold;
                    mediaClient.MaxAutoReconnects = MaximumReconnects;
                },
                dcId => _client.GetClientForDC(-dcId), client => client.Disconnected);
            _privateChannels = new(offset => _client.Messages_GetDialogs(offset?.Date ?? default,
                offset?.MessageId ?? 0, offset?.Peer, limit: 100, exclude_pinned: offset is not null));
        }

        public async Task LoginAsync(bool reloginOnFailedResume)
        {
            try { await _client.LoginUserIfNeeded(reloginOnFailedResume: reloginOnFailedResume).ConfigureAwait(false); }
            catch (RpcException ex) when (ex.Code == 401 && ex.Message is "SESSION_REVOKED" or "SESSION_EXPIRED" or "AUTH_KEY_UNREGISTERED")
            {
                throw new DownloadException(FailureKind.Authentication,
                    $"The saved authorization expired. {ProfileStore.SessionRecovery}", ex);
            }
        }

        public async Task<VideoInfo> ResolveVideoAsync(PostLink link, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Channel? channel;
            if (link.ChannelId is { } id)
            {
                channel = await _privateChannels.FindAsync(id, token).ConfigureAwait(false);
                if (channel is null)
                    throw new DownloadException(FailureKind.AccessDenied,
                        "This account cannot access the private group or channel. Join it in Telegram and copy the post link again.");
            }
            else
            {
                var peer = await _client.Contacts_ResolveUsername(link.Username!).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                channel = peer.chats.TryGetValue(peer.peer.ID, out var chat) ? chat as Channel : null;
                if (channel is null)
                    throw new DownloadException(FailureKind.NotFound, "The link must point to a post in a Telegram group or channel.");
            }
            token.ThrowIfCancellationRequested();
            var response = await _client.Channels_GetMessages(channel, new InputMessageID { id = link.MessageId }).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            var message = response.Messages.OfType<Message>().FirstOrDefault(item => item.id == link.MessageId);
            if (message?.media is not MessageMediaDocument { document: Document document } || document.size <= 0 ||
                !(document.mime_type?.StartsWith("video/", StringComparison.OrdinalIgnoreCase) == true ||
                  document.attributes?.Any(attribute => attribute is DocumentAttributeVideo) == true))
                throw new DownloadException(FailureKind.NotFound,
                    "This exact post is missing or does not contain a downloadable video. Copy the link to the video message itself.");
            // Saving restrictions in Desktop are not used as an invented server-side denial.
            // Attempt this account's document transfer and surface any actual Telegram refusal.
            // Caption remains transient naming input; it is not copied to account/queue history.
            return new(document.Filename ?? string.Empty, document.size, new VideoHandle(link, document),
                message.message, document.mime_type);
        }

        public async Task DownloadAsync(VideoInfo video, Stream output, IProgress<TransferProgress>? progress, CancellationToken token)
        {
            if (video.Handle is not VideoHandle handle)
                throw new DownloadException(FailureKind.InvalidInput, "Resolve the post again before downloading its video.");
            token.ThrowIfCancellationRequested();
            for (var attempt = 0; ; attempt++)
            {
                var dcId = handle.Document.dc_id;
                var mediaClient = await _mediaClients.GetAsync(dcId, token).ConfigureAwait(false);
                var migrations = 0;
                try
                {
                    var location = handle.Document.ToFileLocation();
                    async Task<Upload_FileBase> ReadChunkAsync(long offset, int limit)
                    {
                        try { return await mediaClient.Upload_GetFile(location, offset, limit).ConfigureAwait(false); }
                        catch (Exception ex) when (ex is ObjectDisposedException or
                            RpcException { Code: 401, Message: "SESSION_REVOKED" or "AUTH_KEY_UNREGISTERED" })
                        {
                            _mediaClients.Invalidate(dcId, mediaClient);
                            throw;
                        }
                    }
                    await TransferChunksAsync(handle.Document.size, output, async (offset, limit) =>
                    {
                        Upload_FileBase response;
                        try
                        {
                            response = await ReadChunkAsync(offset, limit).ConfigureAwait(false);
                        }
                        catch (RpcException ex) when (ex.Code == 303 && ex.Message == "FILE_MIGRATE_X" && migrations++ < 2)
                        {
                            token.ThrowIfCancellationRequested();
                            mediaClient = await _mediaClients.GetAsync(ex.X, token).ConfigureAwait(false);
                            dcId = ex.X;
                            token.ThrowIfCancellationRequested();
                            response = await ReadChunkAsync(offset, limit).ConfigureAwait(false);
                        }
                        return response is Upload_File file
                            ? file.bytes
                            : throw new DownloadException(FailureKind.Network,
                                "Telegram returned an unsupported file delivery method. Try again later or update the tool.");
                    }, progress, token).ConfigureAwait(false);
                    return;
                }
                catch (RpcException ex) when (attempt == 0 && ex.Message is "FILE_REFERENCE_EXPIRED" or "FILE_REFERENCE_INVALID")
                {
                    token.ThrowIfCancellationRequested();
                    var refreshed = await ResolveVideoAsync(handle.Link, token).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    var refreshedHandle = (VideoHandle)refreshed.Handle;
                    if (refreshed.Size != video.Size || refreshedHandle.Document.id != handle.Document.id)
                        throw new DownloadException(FailureKind.NotFound, "The post's video changed during the download. Start again to download the updated post.");
                    handle = refreshedHandle;
                    try
                    {
                        output.Position = 0;
                        output.SetLength(0);
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
                    {
                        throw new DownloadException(FailureKind.Destination,
                            "The partial video could not be restarted. Check destination permissions and free disk space, then try again.", error);
                    }
                }
            }
        }

        public ValueTask DisposeAsync()
        {
            // Close admission before main-client disposal walks all alternate DC clients.
            _mediaClients.Close();
            return _client.DisposeAsync();
        }
    }

    internal sealed class MediaClientCache<T>(Func<int, Task<T>> create, Action<T> configure,
        Func<int, Task<T>> connect, Func<T, bool> disconnected) where T : class
    {
        private readonly SemaphoreSlim _initialization = new(1, 1);
        private readonly object _sync = new();
        private readonly Dictionary<int, T> _clients = [];
        private bool _closed;

        public async Task<T> GetAsync(int dcId, CancellationToken token)
        {
            await _initialization.WaitAsync(token).ConfigureAwait(false);
            try
            {
                Task<T> creation;
                lock (_sync)
                {
                    ObjectDisposedException.ThrowIf(_closed, this);
                    token.ThrowIfCancellationRequested();
                    if (_clients.TryGetValue(dcId, out var existing) && !disconnected(existing)) return existing;
                    creation = create(dcId);
                }
                // Once shared initialization starts it drains without a job's cancellation.
                // Close() prevents a later connect/create call racing main-client disposal.
                var client = await creation.ConfigureAwait(false);
                Task<T> connection;
                lock (_sync)
                {
                    ObjectDisposedException.ThrowIf(_closed, this);
                    configure(client);
                    connection = connect(dcId);
                }
                client = await connection.ConfigureAwait(false);
                lock (_sync)
                {
                    ObjectDisposedException.ThrowIf(_closed, this);
                    _clients[dcId] = client;
                }
                token.ThrowIfCancellationRequested();
                return client;
            }
            finally { _initialization.Release(); }
        }

        public void Invalidate(int dcId, T client)
        {
            lock (_sync)
                if (_clients.TryGetValue(dcId, out var cached) && ReferenceEquals(cached, client))
                    _clients.Remove(dcId);
        }

        public void Close()
        {
            lock (_sync) _closed = true;
        }
    }
}

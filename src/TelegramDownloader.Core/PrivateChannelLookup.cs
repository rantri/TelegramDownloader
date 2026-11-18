using TL;

namespace TelegramDownloader.Core;

internal sealed record DialogOffset(DateTime Date, int MessageId, InputPeer? Peer);

// Share pages across jobs without retaining messages or captions.
internal sealed class PrivateChannelLookup(Func<DialogOffset?, Task<Messages_DialogsBase>> getPage)
{
    private readonly SemaphoreSlim _scan = new(1, 1);
    private readonly Dictionary<long, Channel> _channels = [];
    private DialogOffset? _offset;
    private int _completedScans;

    public async Task<Channel?> FindAsync(long id, CancellationToken token)
    {
        var initialScan = Volatile.Read(ref _completedScans);
        while (true)
        {
            await _scan.WaitAsync(token).ConfigureAwait(false);
            try
            {
                token.ThrowIfCancellationRequested();
                if (_channels.TryGetValue(id, out var channel)) return channel;
                // A later retry may see a newly joined channel. Concurrent misses share one scan.
                if (_completedScans != initialScan) return null;

                var response = await getPage(_offset).ConfigureAwait(false);
                if (response is not Messages_Dialogs page)
                    throw new DownloadException(FailureKind.Network, "Telegram could not list the account's chats. Try again.");
                foreach (var dialog in page.dialogs)
                    if (dialog.Peer is PeerChannel peer && page.chats.GetValueOrDefault(peer.ID) is Channel { IsActive: true } found)
                        _channels[peer.ID] = found;
                _offset = page is Messages_DialogsSlice ? NextOffset(page) : null;
                if (_offset is null) Interlocked.Increment(ref _completedScans);
                // Keep this page for other jobs even if its requester cancelled during the RPC.
                token.ThrowIfCancellationRequested();
            }
            finally { _scan.Release(); }
        }
    }

    private DialogOffset? NextOffset(Messages_Dialogs page)
    {
        for (var i = page.dialogs.Length - 1; i >= 0; i--)
        {
            if (page.dialogs[i] is not Dialog dialog || dialog.flags.HasFlag(Dialog.Flags.pinned)) continue;
            var peer = dialog.Peer;
            var date = page.messages.LastOrDefault(message => message.ID == dialog.TopMessage &&
                message.Peer?.GetType() == peer.GetType() && message.Peer.ID == peer.ID)?.Date ?? default;
            if (date == default) continue;
            var nextPeer = page.UserOrChat(peer)?.ToInputPeer();
            if (nextPeer is null) continue;
            if (_offset is { Peer: { } lastPeer } && date == _offset.Date && dialog.TopMessage == _offset.MessageId &&
                nextPeer.GetType() == lastPeer.GetType() && nextPeer.ID == lastPeer.ID) return null;
            return new(date, dialog.TopMessage, nextPeer);
        }
        // A first page containing only pinned dialogs has no chronological offset.
        return _offset is null && page.dialogs.Length > 0 ? new(default, 0, null) : null;
    }
}

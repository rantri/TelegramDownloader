using TelegramDownloader.Core;
using TL;

namespace TelegramDownloader.Tests;

public sealed class PrivateChannelLookupTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly DateTime Date = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task CancellationDrainsOnePageAndPeersReuseBothItsChannelsAndOffset()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pages = 0;
        var lookup = new PrivateChannelLookup(async offset =>
        {
            if (Interlocked.Increment(ref pages) == 1)
            {
                Assert.Null(offset);
                entered.SetResult();
                await release.Task.WaitAsync(Timeout);
                return Page(10, more: true);
            }
            Assert.NotNull(offset);
            Assert.Equal(Date, offset.Date);
            Assert.Equal(42, offset.MessageId);
            Assert.Equal(10, Assert.IsType<InputPeerChannel>(offset.Peer).channel_id);
            return Page(20, more: false);
        });
        using var leader = new CancellationTokenSource();
        var cancelled = lookup.FindAsync(20, leader.Token);
        await entered.Task.WaitAsync(Timeout);
        var peer = lookup.FindAsync(10, default);
        using var waiting = new CancellationTokenSource();
        var cancelledWaiter = lookup.FindAsync(30, waiting.Token);
        waiting.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelledWaiter.WaitAsync(Timeout));
        leader.Cancel();
        Assert.False(cancelled.IsCompleted);
        release.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled.WaitAsync(Timeout));
        Assert.Equal(10, (await peer.WaitAsync(Timeout))!.id);
        Assert.Equal(1, pages);
        Assert.Equal(20, (await lookup.FindAsync(20, default))!.id);
        Assert.Equal(10, (await lookup.FindAsync(10, default))!.id);
        Assert.Equal(2, pages);
    }

    [Fact]
    public async Task PinnedOnlyFirstPageContinuesWithUnpinnedDialogsAndMissesCanBeRetried()
    {
        var pages = 0;
        var lookup = new PrivateChannelLookup(offset =>
        {
            pages++;
            if (pages == 2)
            {
                Assert.NotNull(offset);
                Assert.Null(offset.Peer);
                Assert.Equal(0, offset.MessageId);
                return Task.FromResult<Messages_DialogsBase>(Page(20, more: false));
            }
            Assert.Null(offset);
            var page = Page(pages == 1 ? 10 : 30, more: pages == 1);
            ((Dialog)page.dialogs[0]).flags |= Dialog.Flags.pinned;
            return Task.FromResult<Messages_DialogsBase>(page);
        });
        Assert.Null(await lookup.FindAsync(30, default));
        Assert.Equal(2, pages);
        Assert.Equal(30, (await lookup.FindAsync(30, default))!.id);
        Assert.Equal(3, pages);
    }

    private static Messages_Dialogs Page(long id, bool more)
    {
        Messages_Dialogs page = more ? new Messages_DialogsSlice() : new Messages_Dialogs();
        var peer = new PeerChannel { channel_id = id };
        page.dialogs = [new Dialog { peer = peer, top_message = 42 }];
        page.messages = [new Message { peer_id = peer, id = 42, date = Date }];
        page.chats = new() { [id] = new Channel { id = id, access_hash = 123 } };
        return page;
    }
}

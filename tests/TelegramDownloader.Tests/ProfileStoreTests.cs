using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using TelegramDownloader.Core;

namespace TelegramDownloader.Tests;

public sealed class ProfileStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "TelegramDownloaderTests", Guid.NewGuid().ToString("N"));
    private static AppProfile Example => new(12345, new string('a', 32), "+15555550100");

    [Fact]
    public void ProfileAndSessionRoundTripAreEncryptedAndUserRestricted()
    {
        var store = new ProfileStore(_root);
        Assert.Null(store.Load());
        store.Save(Example);
        Assert.Equal(Example, store.Load());
        using (store.AcquireLease())
        {
            store.WriteSession(Encoding.UTF8.GetBytes("synthetic session secret"));
            store.ThrowIfPersistenceFailed();
            Assert.Equal("synthetic session secret", Encoding.UTF8.GetString(store.ReadSession()!));
        }
        foreach (var path in Directory.GetFiles(_root, "*.dpapi"))
        {
            var bytes = Encoding.UTF8.GetString(File.ReadAllBytes(path));
            Assert.DoesNotContain(Example.ApiHash, bytes);
            Assert.DoesNotContain(Example.PhoneNumber, bytes);
            Assert.DoesNotContain("synthetic session secret", bytes);
        }
        var acl = new DirectoryInfo(_root).GetAccessControl();
        Assert.True(acl.AreAccessRulesProtected);
        var sid = WindowsIdentity.GetCurrent().User!;
        Assert.All(acl.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>(),
            rule => Assert.Equal(sid, rule.IdentityReference));
    }

    [Theory]
    [InlineData(0, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "+15555550100")]
    [InlineData(1, "not-a-hash", "+15555550100")]
    [InlineData(1, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "5555550100")]
    [InlineData(1, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "+abc")]
    public void InvalidProfilesFailBeforeCreatingStorage(int id, string hash, string phone)
    {
        var error = Assert.Throws<DownloadException>(() => new ProfileStore(_root).Save(new(id, hash, phone)));
        Assert.Equal(FailureKind.Authentication, error.Kind);
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public void LeaseExcludesOtherInstancesAndSaveReset()
    {
        var store = new ProfileStore(_root);
        using (store.AcquireLease())
        {
            var other = new ProfileStore(_root);
            Assert.Equal(FailureKind.Busy, Assert.Throws<DownloadException>(() => other.AcquireLease()).Kind);
            Assert.Equal(FailureKind.Busy, Assert.Throws<DownloadException>(() => other.Save(Example)).Kind);
            Assert.Equal(FailureKind.Busy, Assert.Throws<DownloadException>(other.Reset).Kind);
        }
        using var released = new ProfileStore(_root).AcquireLease();
    }

    [Fact]
    public void ReconfigurationRemovesOldSessionButSameProfilePreservesIt()
    {
        var store = new ProfileStore(_root);
        store.Save(Example);
        using (store.AcquireLease()) store.WriteSession([1, 2, 3]);
        store.Save(Example);
        Assert.Equal(new byte[] { 1, 2, 3 }, store.ReadSession());
        store.Save(Example with { PhoneNumber = "+15555550101" });
        Assert.Null(store.ReadSession());
    }

    [Fact]
    public void FailedProfileReplacementRestoresTheOriginalSession()
    {
        var store = new ProfileStore(_root);
        store.Save(Example);
        using (store.AcquireLease()) store.WriteSession([1, 2, 3]);
        using var held = File.Open(Path.Combine(_root, "profile.dpapi"), FileMode.Open, FileAccess.Read, FileShare.Read);
        Assert.Throws<DownloadException>(() => store.Save(Example with { PhoneNumber = "+15555550101" }));
        Assert.Equal(Example, store.Load());
        Assert.Equal(new byte[] { 1, 2, 3 }, store.ReadSession());
        Assert.Empty(Directory.GetFiles(_root, "*.previous.dpapi"));
        Assert.Empty(Directory.GetFiles(_root, "*.pending.dpapi"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NextLeaseRecoversAnInterruptedProfileChange(bool committed)
    {
        var store = new ProfileStore(_root);
        store.Save(Example);
        using (store.AcquireLease()) store.WriteSession([1, 2, 3]);
        var session = Path.Combine(_root, "session.dpapi");
        if (!committed) File.Copy(Path.Combine(_root, "profile.dpapi"), Path.Combine(_root, "profile.pending.dpapi"));
        File.Move(session, Path.Combine(_root, "session.previous.dpapi"));
        using var recovered = store.AcquireLease();
        if (committed) Assert.Null(store.ReadSession());
        else Assert.Equal(new byte[] { 1, 2, 3 }, store.ReadSession());
        Assert.Empty(Directory.GetFiles(_root, "*.previous.dpapi"));
        Assert.Empty(Directory.GetFiles(_root, "*.pending.dpapi"));
    }

    [Fact]
    public void DocumentedRecoveryFromAnUnreadableSessionAllowsFreshSignIn()
    {
        var store = new ProfileStore(_root);
        store.Save(Example);
        File.WriteAllText(Path.Combine(_root, "session.dpapi"), "corrupt");
        var error = Assert.Throws<DownloadException>(() => store.ReadSession());
        Assert.Contains("logout, configure, then login", error.Message);
        Assert.Contains("Forget local account", error.Message);
        store.Reset();
        store.Save(Example);
        Assert.Equal(Example, store.Load());
        Assert.Null(store.ReadSession());
    }

    [Fact]
    public void ResetDeletesOnlyApplicationAccountFiles()
    {
        var store = new ProfileStore(_root);
        store.Save(Example);
        using (store.AcquireLease()) store.WriteSession([1, 2, 3]);
        var unrelated = Path.Combine(_root, "keep.txt");
        File.WriteAllText(unrelated, "keep");
        store.Reset();
        Assert.Null(store.Load());
        Assert.Null(store.ReadSession());
        Assert.Equal("keep", File.ReadAllText(unrelated));
    }

    [Fact]
    public void CorruptedProfileAndSessionAreReportedWithoutSilentlyReplacingThem()
    {
        var store = new ProfileStore(_root);
        store.Save(Example);
        File.WriteAllText(Path.Combine(_root, "profile.dpapi"), "corrupt");
        var error = Assert.Throws<DownloadException>(() => store.Load());
        Assert.Equal(FailureKind.Authentication, error.Kind);
        File.WriteAllText(Path.Combine(_root, "session.dpapi"), "corrupt");
        Assert.Throws<DownloadException>(() => store.ReadSession());
        Assert.Equal("corrupt", File.ReadAllText(Path.Combine(_root, "session.dpapi")));
    }

    [Fact]
    public void SessionCallbackPersistenceFailureIsObservable()
    {
        var store = new ProfileStore(_root);
        using var lease = store.AcquireLease();
        Directory.CreateDirectory(Path.Combine(_root, "session.dpapi"));
        store.WriteSession([1, 2, 3]);
        var error = Assert.Throws<DownloadException>(store.ThrowIfPersistenceFailed);
        Assert.Equal(FailureKind.Authentication, error.Kind);
        Assert.Empty(Directory.GetFiles(_root, "*.tmp"));
    }

    [Fact]
    public void SuccessfulSessionWriteClearsAnEarlierPersistenceFailure()
    {
        var store = new ProfileStore(_root);
        using var lease = store.AcquireLease();
        var obstruction = Path.Combine(_root, "session.dpapi");
        Directory.CreateDirectory(obstruction);
        store.WriteSession([1, 2, 3]);
        Assert.Throws<DownloadException>(store.ThrowIfPersistenceFailed);
        Directory.Delete(obstruction);
        store.WriteSession([4, 5, 6]);
        store.ThrowIfPersistenceFailed();
        Assert.Equal(new byte[] { 4, 5, 6 }, store.ReadSession());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}

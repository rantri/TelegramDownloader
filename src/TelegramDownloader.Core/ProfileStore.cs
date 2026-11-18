using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;

namespace TelegramDownloader.Core;

public sealed class ProfileStore
{
    internal const string SessionRecovery = "Use logout, configure, then login, or choose Forget local account, Set up account, then Sign in.";
    private readonly object _persistenceSync = new();
    private Exception? _persistenceFailure;
    public string RootDirectory { get; }
    private string ProfilePath => Path.Combine(RootDirectory, "profile.dpapi");
    private string SessionPath => Path.Combine(RootDirectory, "session.dpapi");
    private string PendingProfilePath => Path.Combine(RootDirectory, "profile.pending.dpapi");
    private string PreviousSessionPath => Path.Combine(RootDirectory, "session.previous.dpapi");

    public ProfileStore(string? rootDirectory = null)
    {
        RootDirectory = Path.GetFullPath(rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TelegramDownloader"));
    }

    public AppProfile? Load()
    {
        var bytes = ReadProtected(ProfilePath, "Use Configure or account Setup to replace the saved settings.");
        if (bytes is null) return null;
        try
        {
            var profile = JsonSerializer.Deserialize<AppProfile>(bytes)
                ?? throw new JsonException("Account profile is empty.");
            profile.Validate();
            return profile;
        }
        catch (JsonException ex)
        {
            throw StorageError("The saved account configuration is damaged. Use Configure or account Setup to replace it.", ex);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public void Save(AppProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile.Validate();
        using var lease = AcquireLease();
        AppProfile? previous;
        try { previous = Load(); }
        catch (DownloadException ex) when (ex.InnerException is null or JsonException or CryptographicException)
        {
            previous = null;
        }
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(profile);
            try
            {
                WriteProtected(PendingProfilePath, bytes);
                if (previous != profile)
                {
                    try { File.Move(SessionPath, PreviousSessionPath); }
                    catch (FileNotFoundException) { /* First sign-in has no session to set aside. */ }
                }
                File.Move(PendingProfilePath, ProfilePath, overwrite: true);
            }
            catch
            {
                RecoverProfileChange();
                throw;
            }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        catch (Exception ex) when (IsStorageFailure(ex))
        {
            throw StorageError("Could not save the account configuration. Check access to the local application data folder.", ex);
        }
        try { File.Delete(PreviousSessionPath); }
        catch (Exception ex) when (IsStorageFailure(ex))
        {
            throw StorageError("Account settings were saved. Close other programs using the account folder and retry to finish removing the previous session.", ex);
        }
    }

    public void Reset()
    {
        using var lease = AcquireLease();
        try
        {
            File.Delete(SessionPath);
            File.Delete(ProfilePath);
            lock (_persistenceSync) _persistenceFailure = null;
        }
        catch (Exception ex) when (IsStorageFailure(ex))
        {
            throw StorageError("Could not remove the saved account. Close other instances and check folder permissions.", ex);
        }
    }

    public IDisposable AcquireLease()
    {
        try
        {
            EnsurePrivateDirectory();
            var lease = new FileStream(Path.Combine(RootDirectory, "account.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            try
            {
                RecoverProfileChange();
                lock (_persistenceSync) _persistenceFailure = null;
                return lease;
            }
            catch
            {
                lease.Dispose();
                throw;
            }
        }
        catch (IOException ex) when ((ex.HResult & 0xffff) is 32 or 33)
        {
            throw new DownloadException(FailureKind.Busy,
                "The Telegram account is in use by another operation. Close the other operation and try again.", ex);
        }
        catch (Exception ex) when (IsStorageFailure(ex))
        {
            throw StorageError("Could not open private account storage. Check access to the local application data folder.", ex);
        }
    }

    // Moving the staged profile into place is the commit point. The lease prevents
    // another process from opening a session until an interrupted change is reconciled.
    private void RecoverProfileChange()
    {
        if (File.Exists(PendingProfilePath))
        {
            if (File.Exists(PreviousSessionPath))
                File.Move(PreviousSessionPath, SessionPath, overwrite: true);
            File.Delete(PendingProfilePath);
        }
        else if (File.Exists(PreviousSessionPath))
            File.Delete(PreviousSessionPath);
    }

    public byte[]? ReadSession() => ReadProtected(SessionPath, SessionRecovery);

    public void WriteSession(byte[] session)
    {
        ArgumentNullException.ThrowIfNull(session);
        lock (_persistenceSync)
        {
            try
            {
                WriteProtected(SessionPath, session);
                _persistenceFailure = null;
            }
            catch (Exception ex) when (IsStorageFailure(ex)) { _persistenceFailure = ex; }
        }
    }

    public void ThrowIfPersistenceFailed()
    {
        lock (_persistenceSync)
            if (_persistenceFailure is not null)
                throw StorageError("The login session could not be saved. Check free disk space and account-folder permissions before signing in again.", _persistenceFailure);
    }

    private void EnsurePrivateDirectory()
    {
        var directory = Directory.CreateDirectory(RootDirectory);
        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Account storage cannot be a symbolic link or junction.");
        var sid = WindowsIdentity.GetCurrent().User
            ?? throw new UnauthorizedAccessException("Windows user identity is unavailable.");
        var security = new DirectorySecurity();
        security.SetOwner(sid);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None, AccessControlType.Allow));
        directory.SetAccessControl(security);
    }

    private static byte[]? ReadProtected(string path, string recovery)
    {
        try
        {
            return ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        catch (CryptographicException ex)
        {
            throw StorageError($"Saved account data could not be opened by this Windows user. {recovery}", ex);
        }
        catch (Exception ex) when (IsStorageFailure(ex))
        {
            throw StorageError("Could not read saved account data. Check access to the local application data folder and try again.", ex);
        }
    }

    private static void WriteProtected(string path, byte[] bytes)
    {
        var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(encrypted);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static bool IsStorageFailure(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or CryptographicException or System.Security.SecurityException;

    private static DownloadException StorageError(string message, Exception ex) => new(FailureKind.Authentication, message, ex);
}

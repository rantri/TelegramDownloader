using System.Diagnostics;
using System.Globalization;
using System.Text;
using TelegramDownloader.Core;

namespace TelegramDownloader.App;

internal static class Cli
{
    private static string ProductVersion => typeof(Cli).Assembly.GetName().Version!.ToString(3);

    public static async Task<int> RunAsync(string[] args)
    {
        using var cancellation = new CancellationTokenSource();
        var shutdownSync = new object();
        TelegramGateway? ownedGateway = null;
        Task? interruptedShutdown = null;
        void StopOwnedGateway()
        {
            lock (shutdownSync)
            {
                if (ownedGateway is not { } gateway || interruptedShutdown is not null) return;
                interruptedShutdown = Task.Run(async () => await gateway.DisposeAsync());
            }
        }
        ConsoleCancelEventHandler onCancel = (_, e) =>
        {
            e.Cancel = true;
            cancellation.Cancel();
            StopOwnedGateway();
        };
        Console.CancelKeyPress += onCancel;
        try
        {
            if (args.Length == 1 && args[0] is "--help" or "-h" or "help")
            {
                Console.Out.WriteLine(Help);
                return 0;
            }
            if (args.Length == 1 && args[0] is "--version" or "version")
            {
                Console.Out.WriteLine($"TelegramDownloader {ProductVersion}");
                return 0;
            }

            var command = args[0].ToLowerInvariant();
            var overwrite = false;
            if (command == "download")
            {
                if (args.Length is not (3 or 4) || (args.Length == 4 && args[3] != "--overwrite"))
                    throw new DownloadException(FailureKind.InvalidInput,
                        "Usage: TelegramDownloader download <post-link> <existing-folder> [--overwrite]");
                _ = PostLink.Parse(args[1]);
                overwrite = args.Length == 4;
                _ = DownloadService.ValidateDestination(args[2], overwrite);
            }
            else if (command is not ("configure" or "login" or "logout") || args.Length != 1)
            {
                throw new DownloadException(FailureKind.InvalidInput,
                    "Unknown command or arguments. Run TelegramDownloader --help for usage.");
            }

            var store = new ProfileStore();
            var token = cancellation.Token;
            if (command == "configure")
            {
                Console.Error.WriteLine("Obtain your own app credentials at https://my.telegram.org/apps.");
                var apiIdText = ReadValue("API ID", false, token);
                if (!int.TryParse(apiIdText, NumberStyles.None, CultureInfo.InvariantCulture, out var apiId))
                    throw new DownloadException(FailureKind.InvalidInput, "API ID must be a positive number.");
                var profile = new AppProfile(apiId, ReadValue("API hash", true, token),
                    ReadValue("Phone number (including + and country code)", false, token));
                profile.Validate();
                store.Save(profile);
                Console.Out.WriteLine("Account settings saved privately. Run TelegramDownloader login next.");
                return 0;
            }
            if (command == "logout")
            {
                store.Reset();
                Console.Out.WriteLine("Local account settings and session removed. To revoke the server session, use Telegram Settings > Devices.");
                return 0;
            }

            await using var gateway = new TelegramGateway(store, key => ReadChallenge(key, token));
            lock (shutdownSync) ownedGateway = gateway;
            if (token.IsCancellationRequested) StopOwnedGateway();
            token.ThrowIfCancellationRequested();
            if (command == "login")
            {
                await gateway.LoginAsync(token);
                Console.Out.WriteLine("Signed in. The saved session is ready for downloads.");
                return 0;
            }

            Console.Error.WriteLine("Connecting and resolving the video. Press Ctrl+C to cancel.");
            var progress = new ConsoleProgress();
            var output = await new DownloadService(gateway).DownloadAsync(args[1], args[2], overwrite, progress, token);
            Console.Out.WriteLine(output);
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Cancelled. No incomplete download was marked as complete.");
            return 130;
        }
        catch (DownloadException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return ExitCode(exception.Kind);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine("Could not access local files. Check the destination and your user-profile permissions.");
            return 5;
        }
        catch
        {
            Console.Error.WriteLine("The operation failed unexpectedly. Retry, or check your account setup and connection.");
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
            Task? shutdown;
            lock (shutdownSync) shutdown = interruptedShutdown;
            if (shutdown is not null)
            {
                try { await shutdown; }
                catch { /* The owning operation reports its failure or cancellation above. */ }
            }
        }
    }

    private static int ExitCode(FailureKind kind) => kind switch
    {
        FailureKind.InvalidInput => 2,
        FailureKind.Authentication => 3,
        FailureKind.NotFound or FailureKind.AccessDenied => 4,
        FailureKind.Destination => 5,
        FailureKind.Network or FailureKind.RateLimited => 6,
        FailureKind.Busy => 7,
        _ => 1
    };

    private static string ReadChallenge(string key, CancellationToken token) => key switch
    {
        "verification_code" => ReadValue("Telegram verification code", true, token),
        "password" => ReadValue("Telegram two-step verification password", true, token, trim: false),
        "email" => ReadValue("Email address requested by Telegram", false, token),
        "email_verification_code" => ReadValue("Email verification code", true, token),
        _ => throw new DownloadException(FailureKind.Authentication,
            "Telegram requested an unsupported sign-in step. Complete account setup in the official Telegram app, then retry.")
    };

    private static string ReadValue(string label, bool secret, CancellationToken token, bool trim = true)
    {
        token.ThrowIfCancellationRequested();
        if (Console.IsInputRedirected)
            throw new DownloadException(FailureKind.Authentication,
                "Interactive account input is required. Run configure/login in a terminal or use the GUI first.");
        Console.Error.Write($"{label}: ");
        var value = new StringBuilder();
        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (!Console.KeyAvailable)
            {
                token.WaitHandle.WaitOne(30);
                continue;
            }
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Escape)
                throw new OperationCanceledException(token);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.Error.WriteLine();
                var result = trim ? value.ToString().Trim() : value.ToString();
                if (result.Length == 0)
                    throw new DownloadException(FailureKind.InvalidInput, $"{label} cannot be empty.");
                return result;
            }
            if (key.Key == ConsoleKey.Backspace)
            {
                if (value.Length > 0)
                {
                    value.Length--;
                    Console.Error.Write("\b \b");
                }
            }
            else if (!char.IsControl(key.KeyChar) && value.Length < 1024)
            {
                value.Append(key.KeyChar);
                Console.Error.Write(secret ? '*' : key.KeyChar);
            }
        }
    }

    private sealed class ConsoleProgress : IProgress<TransferProgress>
    {
        private readonly Stopwatch _watch = Stopwatch.StartNew();
        private long _lastUpdate = -1000;
        private string? _fileName;
        private readonly object _sync = new();

        public void Report(TransferProgress value)
        {
            lock (_sync)
            {
                if (value.FileName is { } name && name != _fileName)
                {
                    _fileName = name;
                    Console.Error.WriteLine($"Saving as: {name}");
                }
                var now = _watch.ElapsedMilliseconds;
                if (now - _lastUpdate < 500 && value.BytesTransferred < value.TotalBytes)
                    return;
                _lastUpdate = now;
                var percent = value.TotalBytes > 0 ? Math.Clamp(100.0 * value.BytesTransferred / value.TotalBytes, 0, 100) : 0;
                Console.Error.WriteLine($"{percent:F1}%  {value.BytesTransferred / 1048576.0:F1} / {value.TotalBytes / 1048576.0:F1} MiB");
            }
        }
    }

    private static string Help => $"""
        TelegramDownloader {ProductVersion} — personal Telegram video downloads

        Usage:
          TelegramDownloader.exe                         Open the GUI
          TelegramDownloader.exe gui                     Open the GUI
          TelegramDownloader.exe configure               Enter API ID/hash and phone privately
          TelegramDownloader.exe login                   Sign in and save the session
          TelegramDownloader.exe download <link> <existing-folder> [--overwrite]
          TelegramDownloader.exe logout                  Remove local account settings/session
          TelegramDownloader.exe --help | --version

        Download example:
          TelegramDownloader.exe download "https://t.me/c/9000000001/42" "C:\Videos"

        Use your own API credentials from https://my.telegram.org/apps. This tool signs
        in separately; Telegram Desktop may receive the verification code. Codes and
        passwords are prompted privately and are not saved. Account state is encrypted
        for your Windows user under LocalApplicationData\TelegramDownloader.

        Version 2 requires an existing destination folder; exact filename destinations
        are no longer accepted. The original filename or post supplies a safe name.
        Recognizable TV episodes use Show.Name.S01E02.ext, retaining supplied release
        details without guessing missing metadata. Existing files receive a numeric
        suffix unless --overwrite is specified; failed transfers never replace them.
        Only the exact linked message's original video is downloaded, not an entire album.
        Public/private and topic-style HTTPS t.me links are supported; comment links are not.
        Your account must have access. Disabled Desktop save controls do not guarantee
        retrieval: the tool attempts the available download and reports Telegram refusals.

        Press Ctrl+C to cancel (Esc also cancels an account prompt). Configure/login
        require an interactive terminal; unattended downloads can reuse a saved session.
        The CLI downloads one video in the foreground. The GUI supports three parallel
        downloads with Cancel/Retry. Closing a busy window keeps downloads in the tray;
        choose Exit to cancel all downloads and close. Queues are not saved across exits.
        Logout deletes local state only. Revoke a server session in Telegram Settings > Devices.

        Exit codes: 0 success; 2 invalid input; 3 authentication/setup; 4 unavailable video;
        5 local file/integrity error; 6 network/service wait; 7 account busy;
        1 unexpected failure; 130 cancelled.

        Uses WTelegramClient (MIT): https://github.com/wiz0u/WTelegramClient
        Uses .NET and Windows Forms (MIT): https://github.com/dotnet
        The single-file release includes the runtime; native files may extract to the
        .NET temporary cache. No separate runtime installation is required.
        """;
}

using System.Reflection;
using System.Text.Json;
using TelegramDownloader.App;
using TelegramDownloader.Core;

namespace TelegramDownloader.GuiSmoke;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var idle = args.Contains("--idle", StringComparer.OrdinalIgnoreCase);
        var outputArgument = args.FirstOrDefault(arg => !arg.Equals("--idle", StringComparison.OrdinalIgnoreCase));
        var resultPath = Path.GetFullPath(outputArgument ?? DefaultResultPath());
        var temporaryRoot = Path.Combine(Path.GetTempPath(), $"TelegramDownloader-GuiSmoke-{Guid.NewGuid():N}");
        var destination = Path.Combine(temporaryRoot, "videos");
        Directory.CreateDirectory(destination);
        var counters = new TransferCounters();
        var queue = new DownloadQueue(() => new SyntheticGateway(counters));
        ApplicationConfiguration.Initialize();
        using var form = new MainForm(queue, new ProfileStore(Path.Combine(temporaryRoot, "account")))
        {
            Text = "Telegram Downloader — synthetic UI check"
        };
        using var controller = new TrayCommandController(form);
        var hiddenTransitions = 0;
        var restoredTransitions = 0;
        var waitingForRestore = false;
        form.VisibleChanged += (_, _) =>
        {
            if (!form.Visible && queue.IsBusy)
            {
                hiddenTransitions++;
                waitingForRestore = true;
            }
            else if (form.Visible && waitingForRestore)
            {
                restoredTransitions++;
                waitingForRestore = false;
            }
        };
        form.Shown += (_, _) =>
        {
            var fields = Descendants(form).OfType<TextBox>().ToArray();
            fields.Single(control => control.AccessibleName == "Telegram post link").Text = "https://t.me/syntheticcheck/6";
            fields.Single(control => control.AccessibleName == "Destination folder").Text = destination;
            if (!idle)
                for (var message = 1; message <= 5; message++)
                    queue.Enqueue($"https://t.me/syntheticcheck/{message}", destination);
            controller.Show();
        };
        Application.Run(form);
        queue.DisposeAsync().AsTask().GetAwaiter().GetResult();
        var partials = Directory.GetFiles(destination, "*.partial");
        var completedFiles = Directory.GetFiles(destination).Where(path => !path.EndsWith(".partial", StringComparison.OrdinalIgnoreCase)).ToArray();
        var passed = counters.Active == 0 && partials.Length == 0 && completedFiles.Length == 0;
        var result = new
        {
            Passed = passed,
            SeededJobs = idle ? 0 : 5,
            HiddenTransitions = hiddenTransitions,
            RestoredTransitions = restoredTransitions,
            ActiveWriters = counters.Active,
            PeakWriters = counters.Peak,
            StartedTransfers = counters.Started,
            PartialFiles = partials.Length,
            CompletedFiles = completedFiles.Length,
            TemporaryRoot = temporaryRoot,
            Jobs = queue.Jobs.Select(job => new { job.Link, State = job.State.ToString(), job.FileName })
        };
        Directory.CreateDirectory(Path.GetDirectoryName(resultPath)!);
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(resultPath, json);
        Console.WriteLine(json);
        return passed ? 0 : 1;
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static string DefaultResultPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TelegramDownloader.slnx")))
            directory = directory.Parent;
        return Path.Combine(directory?.FullName ?? AppContext.BaseDirectory, "artifacts", "gui-smoke-result.json");
    }
}

internal sealed class TrayCommandController : Form
{
    private readonly MainForm _main;
    private readonly ContextMenuStrip _commands;
    private bool _mainClosed;

    public TrayCommandController(MainForm main)
    {
        _main = main;
        var trayField = typeof(MainForm).GetField("_tray", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("The production tray icon field was not found.");
        var tray = (NotifyIcon?)trayField.GetValue(main)
            ?? throw new InvalidOperationException("The production tray icon was not created.");
        _commands = tray.ContextMenuStrip
            ?? throw new InvalidOperationException("The production tray menu was not created.");
        Text = "Synthetic tray commands";
        Font = new Font("Segoe UI", 10);
        ClientSize = new Size(370, 140);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.Manual;
        Location = new Point(20, 20);
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), FlowDirection = FlowDirection.TopDown, WrapContents = false };
        layout.Controls.Add(new Label
        {
            Text = "Test helper: invokes the production tray menu handlers.", AutoSize = true,
            MaximumSize = new Size(340, 0), Margin = new Padding(0, 0, 0, 12)
        });
        var buttons = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty };
        var show = new Button { Text = "Show download window", AutoSize = true };
        var exit = new Button { Text = "Exit downloads", AutoSize = true };
        show.Click += (_, _) => InvokeCommand("Show");
        exit.Click += (_, _) => InvokeCommand("Exit");
        buttons.Controls.AddRange([show, exit]);
        layout.Controls.Add(buttons);
        Controls.Add(layout);
        main.FormClosed += (_, _) =>
        {
            _mainClosed = true;
            Close();
        };
        FormClosing += (_, e) =>
        {
            if (_mainClosed) return;
            e.Cancel = true;
            InvokeCommand("Exit");
        };
    }

    private void InvokeCommand(string text)
    {
        if (_mainClosed || _main.IsDisposed) return;
        _commands.Items.OfType<ToolStripMenuItem>().Single(item => item.Text == text).PerformClick();
    }
}

internal sealed class TransferCounters
{
    private readonly object _sync = new();
    public int Active { get; private set; }
    public int Peak { get; private set; }
    public int Started { get; private set; }

    public void Enter()
    {
        lock (_sync)
        {
            Active++;
            Started++;
            Peak = Math.Max(Peak, Active);
        }
    }

    public void Leave()
    {
        lock (_sync) Active--;
    }
}

internal sealed class SyntheticGateway(TransferCounters counters) : ITelegramGateway
{
    private readonly CancellationTokenSource _stop = new();
    private readonly object _sync = new();
    private readonly TaskCompletionSource _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _writers;
    private bool _disposed;

    public Task LoginAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task<VideoInfo> ResolveVideoAsync(PostLink link, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return Task.FromResult(new VideoInfo($"Synthetic.Show.S01E{link.MessageId:00}.mkv", 1024 * 1024,
            link.MessageId, MimeType: "video/x-matroska"));
    }

    public async Task DownloadAsync(VideoInfo video, Stream output, IProgress<TransferProgress>? progress, CancellationToken token)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _writers++;
        }
        counters.Enter();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _stop.Token);
        try
        {
            await output.WriteAsync(new byte[64 * 1024], linked.Token);
            progress?.Report(new TransferProgress(64 * 1024, video.Size, video.FileName));
            await Task.Delay(Timeout.Infinite, linked.Token);
        }
        finally
        {
            counters.Leave();
            lock (_sync)
            {
                _writers--;
                if (_disposed && _writers == 0) _drained.TrySetResult();
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            if (!_disposed)
            {
                _disposed = true;
                _stop.Cancel();
            }
            if (_writers == 0) _drained.TrySetResult();
            return new ValueTask(_drained.Task);
        }
    }
}

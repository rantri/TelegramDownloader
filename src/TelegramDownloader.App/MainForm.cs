using TelegramDownloader.Core;

namespace TelegramDownloader.App;

internal sealed class MainForm : Form
{
    private readonly ProfileStore _store;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DownloadQueue _queue;
    private readonly System.Windows.Forms.Timer _refresh = new() { Interval = 250 };
    private readonly ContextMenuStrip _trayMenu = new();
    private readonly NotifyIcon _tray;
    private readonly Dictionary<Guid, DataGridViewRow> _rows = [];
    private readonly TextBox _link = new() { Dock = DockStyle.Fill, PlaceholderText = "https://t.me/c/9000000001/42" };
    private readonly TextBox _destination = new() { Dock = DockStyle.Fill, PlaceholderText = "Choose an existing destination folder" };
    private readonly CheckBox _overwrite = new() { AutoSize = true, Text = "Replace an existing file only after a successful download" };
    private readonly Button _browse = new() { Text = "Browse folder…", AutoSize = true };
    private readonly Button _setup = new() { Text = "Set up account", AutoSize = true };
    private readonly Button _signIn = new() { Text = "Sign in", AutoSize = true };
    private readonly Button _forget = new() { Text = "Forget local account", AutoSize = true };
    private readonly Button _add = new() { Text = "Add download", AutoSize = true, Padding = new Padding(12, 5, 12, 5) };
    private readonly Button _cancel = new() { Text = "Cancel selected", AutoSize = true, Enabled = false };
    private readonly Button _retry = new() { Text = "Retry selected", AutoSize = true, Enabled = false };
    private readonly Button _exit = new() { Text = "Exit", AutoSize = true };
    private readonly Label _account = new() { AutoSize = true };
    private readonly Label _summary = new() { AutoSize = true, Margin = new Padding(0, 5, 0, 7) };
    private readonly DataGridView _jobs = new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
        AllowUserToDeleteRows = false, AllowUserToResizeRows = false, RowHeadersVisible = false,
        MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BackgroundColor = Color.White,
        BorderStyle = BorderStyle.FixedSingle, AutoGenerateColumns = false
    };
    private readonly TextBox _details = new()
    {
        Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
        BackColor = Color.White, Height = 92
    };
    private readonly Label _status = new() { Dock = DockStyle.Fill, AutoSize = true, MaximumSize = new Size(0, 66) };
    private readonly object _accountSync = new();
    private CancellationTokenSource? _accountCancellation;
    private ITelegramGateway? _accountGateway;
    private Task<string>? _accountTask;
    private bool _accountBusy;
    private bool _exiting;
    private bool _allowClose;
    private string? _lastSessionError;

    public MainForm(DownloadQueue? queue = null, ProfileStore? store = null)
    {
        _store = store ?? new ProfileStore();
        _queue = queue ?? new DownloadQueue(() => new TelegramGateway(_store,
            key => PromptForChallenge(key, _lifetime.Token)));
        _tray = new NotifyIcon
        {
            Icon = SystemIcons.Application, Text = "Telegram Downloader", Visible = true,
            ContextMenuStrip = _trayMenu
        };
        Text = "Telegram Downloader";
        TextChanged += (_, _) => _tray.Text = Text.Length <= 63 ? Text : Text[..63];
        Font = new Font("Segoe UI", 10);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(900, 760);
        ClientSize = new Size(1100, 800);
        BackColor = Color.FromArgb(245, 247, 250);
        BuildLayout();
        _trayMenu.Items.Add("Show", null, (_, _) => RestoreWindow());
        _trayMenu.Items.Add("Exit", null, async (_, _) => await RequestExitAsync());
        _tray.DoubleClick += (_, _) => RestoreWindow();
        _setup.Click += async (_, _) => await ConfigureAsync();
        _signIn.Click += async (_, _) => await RunAccountOperationAsync("Signing in…", SignInAsync);
        _forget.Click += async (_, _) =>
        {
            if (MessageBox.Show(this, "Remove this tool's saved account settings and session?\n\nThis does not sign out Telegram Desktop.",
                    "Forget local account", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            await RunAccountOperationAsync("Removing local account…", token =>
            {
                token.ThrowIfCancellationRequested();
                _store.Reset();
                return Task.FromResult("Local settings and session removed. Revoke the server session in Telegram Settings > Devices if needed.");
            });
        };
        _browse.Click += (_, _) => BrowseDestination();
        _add.Click += (_, _) => AddDownload();
        _cancel.Click += (_, _) =>
        {
            if (SelectedJob() is { } job) _queue.Cancel(job.Id);
            RefreshQueue();
        };
        _retry.Click += (_, _) => RetrySelected();
        _exit.Click += async (_, _) => await RequestExitAsync();
        _jobs.SelectionChanged += (_, _) => RefreshSelection();
        _refresh.Tick += (_, _) => RefreshQueue();
        FormClosing += OnClosing;
        RefreshAccount();
        RefreshQueue();
        _refresh.Start();
        SetStatus("Choose a folder and add post links. Up to three downloads run together; filenames are chosen automatically.");
    }

    private void BuildLayout()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 13
        };
        for (var i = 0; i < 13; i++)
            layout.RowStyles.Add(new RowStyle(i == 9 ? SizeType.Percent : SizeType.AutoSize, i == 9 ? 100 : 0));
        layout.Controls.Add(new Label
        {
            Text = "Your video downloads", Font = new Font("Segoe UI", 22, FontStyle.Bold),
            ForeColor = Color.FromArgb(30, 55, 80), AutoSize = true, Margin = new Padding(0, 0, 0, 5)
        }, 0, 0);
        layout.Controls.Add(new Label
        {
            Text = "Original videos, automatic filenames. Add more while downloads run.", AutoSize = true,
            ForeColor = Color.FromArgb(85, 98, 110), Margin = new Padding(0, 0, 0, 14)
        }, 0, 1);
        var accountBox = new GroupBox { Text = "Telegram account", Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(10), Margin = new Padding(0, 0, 0, 12) };
        var accountLayout = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, RowCount = 2 };
        _account.Margin = new Padding(0, 2, 0, 6);
        accountLayout.Controls.Add(_account);
        var accountButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Margin = Padding.Empty };
        accountButtons.Controls.AddRange([_setup, _signIn, _forget]);
        accountLayout.Controls.Add(accountButtons);
        accountBox.Controls.Add(accountLayout);
        layout.Controls.Add(accountBox, 0, 2);
        layout.Controls.Add(new Label { Text = "Post link", AutoSize = true, Margin = new Padding(0, 0, 0, 3) }, 0, 3);
        _link.Margin = new Padding(0, 0, 0, 9);
        layout.Controls.Add(_link, 0, 4);
        layout.Controls.Add(new Label { Text = "Destination folder", AutoSize = true, Margin = new Padding(0, 0, 0, 3) }, 0, 5);
        var destinationRow = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 0, 0, 6) };
        destinationRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        destinationRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _destination.Margin = new Padding(0, 3, 8, 3);
        destinationRow.Controls.Add(_destination, 0, 0);
        destinationRow.Controls.Add(_browse, 1, 0);
        layout.Controls.Add(destinationRow, 0, 6);
        _overwrite.Margin = new Padding(0, 0, 0, 9);
        layout.Controls.Add(_overwrite, 0, 7);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 0, 0, 9) };
        _add.BackColor = Color.FromArgb(29, 104, 166);
        _add.ForeColor = Color.White;
        _add.FlatStyle = FlatStyle.Flat;
        _add.FlatAppearance.BorderSize = 0;
        actions.Controls.AddRange([_add, _cancel, _retry, _exit]);
        layout.Controls.Add(actions, 0, 8);
        AddColumn("Video / post link", 210, 31);
        AddColumn("State", 95, 12);
        AddColumn("Progress", 72, 9);
        AddColumn("Transferred", 135, 18);
        AddColumn("Destination", 170, 30);
        _jobs.Margin = Padding.Empty;
        layout.Controls.Add(_jobs, 0, 9);
        layout.Controls.Add(_summary, 0, 10);
        _details.Margin = new Padding(0, 0, 0, 8);
        layout.Controls.Add(_details, 0, 11);
        _status.Margin = Padding.Empty;
        layout.Controls.Add(_status, 0, 12);
        Controls.Add(layout);
        AcceptButton = _add;
        _link.AccessibleName = "Telegram post link";
        _destination.AccessibleName = "Destination folder";
        _jobs.AccessibleName = "Download queue";
        _details.AccessibleName = "Selected download link, path and error";
        _status.AccessibleName = "Operation result";
    }

    private void AddColumn(string title, int minimumWidth, float weight) => _jobs.Columns.Add(new DataGridViewTextBoxColumn
    {
        HeaderText = title, MinimumWidth = minimumWidth, FillWeight = weight,
        SortMode = DataGridViewColumnSortMode.NotSortable
    });

    private void AddDownload()
    {
        if (_exiting || _accountBusy) return;
        try
        {
            var id = _queue.Enqueue(_link.Text.Trim(), _destination.Text.Trim(), _overwrite.Checked);
            RefreshQueue();
            SelectJob(id);
            SetStatus("Added to the queue. You can add another post link while downloads continue.");
        }
        catch (Exception exception) { SetStatus(SafeMessage(exception), true); }
    }

    private void RetrySelected()
    {
        if (_exiting || _accountBusy || SelectedJob() is not { } job) return;
        try
        {
            var id = _queue.Retry(job.Id);
            RefreshQueue();
            SelectJob(id);
            SetStatus("Retry added as a new download. The original result remains in the list.");
        }
        catch (Exception exception) { SetStatus(SafeMessage(exception), true); }
    }

    private void SelectJob(Guid id)
    {
        if (!_rows.TryGetValue(id, out var row)) return;
        _jobs.ClearSelection();
        _jobs.CurrentCell = row.Cells[0];
        row.Selected = true;
        RefreshSelection();
    }

    private void RefreshQueue()
    {
        if (IsDisposed) return;
        var snapshots = _queue.Jobs;
        foreach (var job in snapshots)
        {
            if (!_rows.TryGetValue(job.Id, out var row))
            {
                row = _jobs.Rows[_jobs.Rows.Add()];
                _rows.Add(job.Id, row);
            }
            row.Tag = job;
            SetCell(row, 0, job.FileName ?? job.Link);
            SetCell(row, 1, job.State.ToString());
            SetCell(row, 2, job.TotalBytes > 0 ? $"{Math.Clamp(100d * job.BytesTransferred / job.TotalBytes, 0, 100):F1}%" : "—");
            SetCell(row, 3, job.TotalBytes > 0 ? $"{job.BytesTransferred / 1048576d:F1} / {job.TotalBytes / 1048576d:F1} MiB" : "—");
            SetCell(row, 4, job.OutputPath ?? job.DestinationFolder);
            row.Cells[0].ToolTipText = job.Link;
            row.Cells[4].ToolTipText = job.Error ?? job.OutputPath ?? job.DestinationFolder;
            row.DefaultCellStyle.ForeColor = job.State == DownloadJobState.Failed ? Color.FromArgb(165, 40, 40) : Color.FromArgb(40, 52, 65);
        }
        var active = snapshots.Count(job => job.State is DownloadJobState.Resolving or DownloadJobState.Downloading or DownloadJobState.Cancelling);
        var waiting = snapshots.Count(job => job.State == DownloadJobState.Queued);
        var completed = snapshots.Count(job => job.State == DownloadJobState.Completed);
        var failed = snapshots.Count(job => job.State == DownloadJobState.Failed);
        var cancelled = snapshots.Count(job => job.State == DownloadJobState.Cancelled);
        _summary.Text = $"{active} active / {_queue.MaximumConcurrency} maximum · {waiting} queued · {completed} completed · {failed} failed · {cancelled} cancelled";
        if (_queue.SessionError != _lastSessionError)
        {
            _lastSessionError = _queue.SessionError;
            if (_lastSessionError is { } error) SetStatus(error, true);
        }
        var canEditAccount = !_exiting && !_accountBusy && !_queue.IsBusy;
        _setup.Enabled = _signIn.Enabled = _forget.Enabled = canEditAccount;
        foreach (var control in new Control[] { _link, _destination, _browse, _overwrite, _add })
            control.Enabled = !_exiting && !_accountBusy;
        _exit.Enabled = !_exiting;
        RefreshSelection();
    }

    private static void SetCell(DataGridViewRow row, int column, string text)
    {
        if (!Equals(row.Cells[column].Value, text)) row.Cells[column].Value = text;
    }

    private DownloadJobSnapshot? SelectedJob() => _jobs.CurrentRow?.Tag as DownloadJobSnapshot;

    private void RefreshSelection()
    {
        var job = SelectedJob();
        _cancel.Enabled = !_exiting && job?.State is DownloadJobState.Queued or DownloadJobState.Resolving or DownloadJobState.Downloading;
        _retry.Enabled = !_exiting && !_accountBusy && job?.State is DownloadJobState.Failed or DownloadJobState.Cancelled;
        var detail = job is null ? "Select a download to see its complete link, destination and result." :
            $"Post: {job.Link}\r\nDestination: {job.OutputPath ?? Path.Combine(job.DestinationFolder, job.FileName ?? "")}" +
            (job.Error is { } error ? $"\r\nError: {error}" : job.State == DownloadJobState.Cancelling ? "\r\nCancelling safely after the current network request finishes…" : $"\r\nState: {job.State}");
        if (_details.Text != detail) _details.Text = detail;
    }

    private async Task ConfigureAsync()
    {
        if (_exiting || _accountBusy || _queue.IsBusy) return;
        try
        {
            AppProfile? existing;
            try { existing = _store.Load(); }
            catch (DownloadException) { existing = null; }
            using var dialog = new AccountSetupDialog(existing);
            if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Profile is not { } profile) return;
            await RunAccountOperationAsync("Saving account settings…", token =>
            {
                token.ThrowIfCancellationRequested();
                _store.Save(profile);
                return Task.FromResult("Account settings saved privately. Choose Sign in to complete Telegram verification.");
            });
        }
        catch (Exception exception) { SetStatus(SafeMessage(exception), true); }
    }

    private async Task<string> SignInAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await using var gateway = new TelegramGateway(_store, key => PromptForChallenge(key, token));
        lock (_accountSync)
        {
            token.ThrowIfCancellationRequested();
            _accountGateway = gateway;
        }
        try
        {
            await gateway.LoginAsync(token);
            return "Signed in. Your session is saved and ready for downloads.";
        }
        finally { lock (_accountSync) _accountGateway = null; }
    }

    private async Task RunAccountOperationAsync(string initialStatus, Func<CancellationToken, Task<string>> action)
    {
        if (_exiting || _accountBusy || _queue.IsBusy) return;
        _accountBusy = true;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _accountCancellation = cancellation;
        RefreshQueue();
        SetStatus(initialStatus);
        try
        {
            _accountTask = Task.Run(() => action(cancellation.Token), cancellation.Token);
            var result = await _accountTask;
            if (!_exiting) SetStatus(result);
        }
        catch (OperationCanceledException)
        {
            if (!_exiting) SetStatus("Account operation cancelled.");
        }
        catch (Exception exception)
        {
            if (!_exiting) SetStatus(SafeMessage(exception), true);
        }
        finally
        {
            _accountTask = null;
            _accountCancellation = null;
            _accountBusy = false;
            if (!_exiting && !IsDisposed)
            {
                RefreshAccount();
                RefreshQueue();
            }
        }
    }

    private string? PromptForChallenge(string key, CancellationToken token)
    {
        var (label, instruction, secret) = key switch
        {
            "verification_code" => ("Telegram verification", "Enter the verification code from Telegram. Check Telegram Desktop or your other signed-in devices.", true),
            "password" => ("Two-step verification", "Enter your Telegram two-step verification password. It will not be saved.", true),
            "email" => ("Email verification", "Enter the email address requested by Telegram for sign-in verification.", false),
            "email_verification_code" => ("Email verification", "Enter the verification code sent to your email address.", true),
            _ => throw new DownloadException(FailureKind.Authentication,
                "Telegram requested an unsupported sign-in step. Complete setup in the official Telegram app, then retry.")
        };
        token.ThrowIfCancellationRequested();
        string? value = null;
        Invoke((Action)(() =>
        {
            if (token.IsCancellationRequested) return;
            RestoreWindow();
            using var dialog = new PromptDialog(label, instruction, secret, preserveWhitespace: key == "password");
            using var registration = token.Register(() =>
            {
                if (!IsDisposed && IsHandleCreated)
                    BeginInvoke((Action)(() =>
                    {
                        if (dialog.IsDisposed) return;
                        dialog.DialogResult = DialogResult.Cancel;
                        dialog.Close();
                    }));
            });
            if (dialog.ShowDialog(this) == DialogResult.OK)
                value = key == "password" ? dialog.Value : dialog.Value.Trim();
        }));
        token.ThrowIfCancellationRequested();
        return value ?? throw new OperationCanceledException(token);
    }

    private void BrowseDestination()
    {
        using var dialog = new FolderBrowserDialog { Description = "Choose a folder for downloaded videos", UseDescriptionForTitle = true };
        if (Directory.Exists(_destination.Text)) dialog.SelectedPath = _destination.Text;
        if (dialog.ShowDialog(this) == DialogResult.OK) _destination.Text = dialog.SelectedPath;
    }

    private void RestoreWindow()
    {
        if (_exiting) return;
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        if (_exiting) return;
        if (_queue.IsBusy && e.CloseReason == CloseReason.UserClosing)
        {
            Hide();
            _tray.ShowBalloonTip(4000, "Downloads continue", "Telegram Downloader is still running. Use the tray icon to show downloads or Exit to cancel and close.", ToolTipIcon.Info);
            return;
        }
        BeginInvoke((Action)(async () => await RequestExitAsync()));
    }

    private async Task RequestExitAsync()
    {
        if (_exiting) return;
        _exiting = true;
        RefreshQueue();
        SetStatus("Cancelling downloads and closing the account session safely…");
        _lifetime.Cancel();
        _accountCancellation?.Cancel();
        try
        {
            var queueShutdown = _queue.DisposeAsync().AsTask();
            ITelegramGateway? accountGateway;
            lock (_accountSync) accountGateway = _accountGateway;
            await Task.WhenAll(queueShutdown, accountGateway?.DisposeAsync().AsTask() ?? Task.CompletedTask,
                _accountTask ?? Task.CompletedTask);
        }
        catch
        {
            // Queue jobs retain their individual results; shutdown must still release the window and tray.
        }
        _allowClose = true;
        Close();
    }

    private void RefreshAccount()
    {
        try
        {
            _account.Text = _store.Load() is null ? "Set up your Telegram app credentials to begin."
                : "Account settings saved. Sign in once, then reuse your session.";
        }
        catch { _account.Text = "Account settings could not be read. Check user-profile access or set up again."; }
    }

    private void SetStatus(string text, bool error = false)
    {
        _status.ForeColor = error ? Color.FromArgb(165, 40, 40) : Color.FromArgb(60, 72, 84);
        _status.Text = text;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refresh.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
            _trayMenu.Dispose();
            _lifetime.Dispose();
        }
        base.Dispose(disposing);
    }

    private static string SafeMessage(Exception exception) => exception switch
    {
        DownloadException known => known.Message,
        IOException or UnauthorizedAccessException => "Could not access local files. Check the destination and your user-profile permissions.",
        _ => "The operation failed unexpectedly. Retry, or check your account setup and connection."
    };
}

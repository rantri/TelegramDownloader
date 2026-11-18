using System.Diagnostics;
using System.Globalization;
using TelegramDownloader.Core;

namespace TelegramDownloader.App;

internal sealed class PromptDialog : Form
{
    private readonly TextBox _value = new() { Dock = DockStyle.Fill };
    public string Value => _value.Text;

    public PromptDialog(string title, string instruction, bool secret, bool preserveWhitespace = false)
    {
        Text = title;
        Font = new Font("Segoe UI", 10);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(480, 186);
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 3
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label { Text = instruction, Dock = DockStyle.Fill, AutoSize = true }, 0, 0);
        _value.UseSystemPasswordChar = secret;
        _value.MaxLength = 1024;
        _value.Margin = new Padding(0, 8, 0, 16);
        layout.Controls.Add(_value, 0, 1);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        var okay = new Button { Text = "Continue", AutoSize = true, DialogResult = DialogResult.None };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        okay.Click += (_, _) =>
        {
            if (_value.Text.Length == 0 || (!preserveWhitespace && string.IsNullOrWhiteSpace(_value.Text)))
            {
                MessageBox.Show(this, "Enter a value, or choose Cancel.", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                _value.Focus();
                return;
            }
            DialogResult = DialogResult.OK;
        };
        buttons.Controls.Add(okay);
        buttons.Controls.Add(cancel);
        layout.Controls.Add(buttons, 0, 2);
        Controls.Add(layout);
        AcceptButton = okay;
        CancelButton = cancel;
        Shown += (_, _) => _value.Focus();
    }

}

internal sealed class AccountSetupDialog : Form
{
    private readonly TextBox _apiId = new() { Dock = DockStyle.Fill, MaxLength = 20 };
    private readonly TextBox _apiHash = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true, MaxLength = 128 };
    private readonly TextBox _phone = new() { Dock = DockStyle.Fill, MaxLength = 64 };

    public AppProfile? Profile { get; private set; }

    public AccountSetupDialog(AppProfile? existing)
    {
        Text = "Set up Telegram account";
        Font = new Font("Segoe UI", 10);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(530, 352);
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(22), ColumnCount = 1, RowCount = 9
        };
        layout.Controls.Add(new Label
        {
            Text = "Use your own Telegram app credentials. Settings are stored privately for your Windows user.",
            AutoSize = true, MaximumSize = new Size(480, 0), Margin = new Padding(0, 0, 0, 8)
        });
        var link = new LinkLabel
        {
            Text = "Get app credentials at my.telegram.org/apps", AutoSize = true, Margin = new Padding(0, 0, 0, 16)
        };
        link.LinkClicked += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo("https://my.telegram.org/apps") { UseShellExecute = true }); }
            catch { MessageBox.Show(this, "Open https://my.telegram.org/apps in your browser.", Text); }
        };
        layout.Controls.Add(link);
        AddField(layout, "API ID", _apiId);
        AddField(layout, "API hash", _apiHash);
        AddField(layout, "Phone number (including + and country code)", _phone);
        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 16, 0, 0)
        };
        var save = new Button { Text = "Save settings", AutoSize = true };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        save.Click += (_, _) =>
        {
            try
            {
                if (!int.TryParse(_apiId.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var id))
                    throw new DownloadException(FailureKind.InvalidInput, "API ID must be a positive number.");
                var profile = new AppProfile(id, _apiHash.Text.Trim(), _phone.Text.Trim());
                profile.Validate();
                Profile = profile;
                DialogResult = DialogResult.OK;
            }
            catch (DownloadException exception)
            {
                MessageBox.Show(this, exception.Message, "Check account settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);
        layout.Controls.Add(buttons);
        Controls.Add(layout);
        AcceptButton = save;
        CancelButton = cancel;
        if (existing is not null)
        {
            _apiId.Text = existing.ApiId.ToString(CultureInfo.InvariantCulture);
            _apiHash.Text = existing.ApiHash;
            _phone.Text = existing.PhoneNumber;
        }
    }

    private static void AddField(TableLayoutPanel layout, string caption, Control input)
    {
        layout.Controls.Add(new Label { Text = caption, AutoSize = true, Margin = new Padding(0, 0, 0, 3) });
        input.Margin = new Padding(0, 0, 0, 10);
        layout.Controls.Add(input);
    }
}

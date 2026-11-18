using System.Runtime.InteropServices;

namespace TelegramDownloader.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 0 || (args.Length == 1 && args[0].Equals("gui", StringComparison.OrdinalIgnoreCase)))
        {
            FreeConsole();
            ApplicationConfiguration.Initialize();
            try
            {
                Application.Run(new MainForm());
                return 0;
            }
            catch
            {
                MessageBox.Show("The application could not start. Check access to your local user profile and try again.",
                    "Telegram Downloader", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        return Cli.RunAsync(args).GetAwaiter().GetResult();
    }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeConsole();
}

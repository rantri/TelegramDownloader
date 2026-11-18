namespace TelegramDownloader.Core;

/// <summary>Reserves a name in the destination directory for the duration of a transfer.</summary>
internal sealed class OutputPathReservation : IDisposable
{
    private FileStream? _reservation;
    private readonly string _folder;
    private readonly string _fileName;
    private bool _overwrite;
    public string Path { get; private set; } = "";

    private OutputPathReservation(string folder, string fileName) { _folder = folder; _fileName = fileName; }

    public static OutputPathReservation Acquire(string folder, string fileName, bool overwrite)
    {
        var reservation = new OutputPathReservation(folder, fileName);
        reservation.SelectPath(overwrite);
        return reservation;
    }

    private void SelectPath(bool overwrite)
    {
        var stem = System.IO.Path.GetFileNameWithoutExtension(_fileName);
        var extension = System.IO.Path.GetExtension(_fileName);
        for (var number = 1; ; number++)
        {
            var name = number == 1 ? _fileName : $"{stem} ({number}){extension}";
            var candidate = System.IO.Path.Combine(_folder, name);
            // Explicit replacement applies only to the proposed name, never to another job's name.
            var replace = overwrite && number == 1;
            if (Directory.Exists(candidate) || (!replace && File.Exists(candidate))) continue;
            // CreateNew arbitrates between all paths to this directory, including junctions and shares.
            // DeleteOnClose also releases the reservation if the process terminates.
            var marker = System.IO.Path.Combine(_folder, $".telegram-{name.ToUpperInvariant()}.lock");
            try
            {
                _reservation = new FileStream(marker, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, 1, FileOptions.DeleteOnClose);
            }
            catch (IOException ex) when ((ex.HResult & 0xffff) is 32 or 80 or 183) { continue; }
            catch (UnauthorizedAccessException) when (File.Exists(marker) || Directory.Exists(marker)) { continue; }
            Path = candidate;
            _overwrite = replace;
            return;
        }
    }

    public string Publish(string partial)
    {
        ObjectDisposedException.ThrowIf(_reservation is null, this);
        while (true)
        {
            try { File.Move(partial, Path, _overwrite); return Path; }
            catch (IOException) when (!_overwrite && (File.Exists(Path) || Directory.Exists(Path)))
            {
                Dispose();
                SelectPath(overwrite: false);
            }
        }
    }

    public void Dispose()
    {
        _reservation?.Dispose();
        _reservation = null;
    }
}

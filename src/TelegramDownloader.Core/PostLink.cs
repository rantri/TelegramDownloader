using System.Globalization;
using System.Text.RegularExpressions;

namespace TelegramDownloader.Core;

public sealed record PostLink(string? Username, long? ChannelId, int MessageId)
{
    public static PostLink Parse(string value)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !(uri.Host.Equals("t.me", StringComparison.OrdinalIgnoreCase) ||
              uri.Host.Equals("telegram.me", StringComparison.OrdinalIgnoreCase)) ||
            !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0)
            throw Invalid();

        foreach (var parameter in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var key = Uri.UnescapeDataString(parameter.Split('=', 2)[0]);
            if (key is not ("single" or "t"))
                throw new DownloadException(FailureKind.InvalidInput,
                    "Use a direct Telegram post link. Comment, invitation, and other query links are not supported.");
        }
        var parts = uri.AbsolutePath[1..].Split('/');
        if (parts.Length < 2) throw Invalid();
        var isPrivate = parts[0] == "c";
        var prefix = isPrivate ? 2 : 1;
        if (parts.Length != prefix + 1 && parts.Length != prefix + 2) throw Invalid();
        if (!PositiveInt(parts[^1], out var message)) throw Invalid();
        if (parts.Length == prefix + 2 && !PositiveInt(parts[^2], out _)) throw Invalid();

        if (isPrivate)
        {
            if (!long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
                throw Invalid();
            return new PostLink(null, id, message);
        }
        if (!Regex.IsMatch(parts[0], "^[A-Za-z][A-Za-z0-9_]{2,31}$", RegexOptions.CultureInvariant) ||
            parts[0].ToLowerInvariant() is "joinchat" or "addstickers" or "addemoji" or "share" or "proxy" or "socks")
            throw Invalid();
        return new PostLink(parts[0], null, message);
    }

    private static bool PositiveInt(string value, out int number) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out number) && number > 0;

    private static DownloadException Invalid() => new(FailureKind.InvalidInput,
        "Use an HTTPS Telegram post link such as https://t.me/channel/42 or https://t.me/c/9000000001/42.");
}

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TelegramDownloader.Core;

/// <summary>Derives a conservative local name using only metadata supplied with the post.</summary>
public static class VideoFileNamer
{
    private const int MaximumNameLength = 180;
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".mp4", ".mkv", ".webm", ".avi", ".mov", ".m4v", ".ts", ".mts", ".m2ts", ".mpeg", ".mpg", ".wmv", ".flv", ".3gp", ".ogv" };
    private static readonly IReadOnlyDictionary<string, string> MimeExtensions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["video/mp4"] = ".mp4", ["video/x-matroska"] = ".mkv", ["video/webm"] = ".webm",
        ["video/quicktime"] = ".mov", ["video/x-msvideo"] = ".avi", ["video/mpeg"] = ".mpeg",
        ["video/mp2t"] = ".ts", ["video/x-ms-wmv"] = ".wmv", ["video/x-flv"] = ".flv",
        ["video/3gpp"] = ".3gp", ["video/ogg"] = ".ogv"
    };
    private static Regex Pattern(string pattern) => new(pattern,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex GenericName = Pattern(@"^(?:video|file|document)[\s._-]*\d*$");
    private static readonly Regex SeasonEpisode = Pattern(@"(?<![\p{L}\p{N}])S(?<season>[0-9]{1,3})[ ._-]*E(?<episode>[0-9]{1,4})(?<more>(?:[ ._]*E[0-9]{1,4}|[ ._]*[\p{Pd}\u2212][ ._]*E?[0-9]{1,4})*)(?![\p{L}\p{N}])");
    private static readonly Regex CrossNotation = Pattern(@"(?<![\p{L}\p{N}])(?<season>[0-9]{1,3})x(?<episode>[0-9]{1,4})(?![\p{L}\p{N}])");
    private static readonly Regex LabelNotation = Pattern(@"(?:Season|עונה)\s*(?<season>[0-9]{1,3})[\s,.:_-]*(?:Episodes?|פרקים|פרק)\s*(?<episode>[0-9]{1,4})(?![\p{L}\p{N}])");
    private static readonly Regex MoreEpisodes = Pattern(@"(?:(?<range>[\p{Pd}\u2212])E?|E)(?<episode>[0-9]{1,4})");
    private static readonly Regex EpisodeOnly = Pattern(@"^(?:S\d|\d+x\d|Season\s*\d|Episodes?\s*\d|עונה\s*\d|פרקים?\s*\d)");
    private static readonly Regex ReleaseExtension = Pattern(@"^\.(?:[0-9]{3,4}[pi]|[248]K|WEB(?:-?DL|Rip)?|BluRay|BDRip|HDTV|UHD|HDR(?:10\+?)?|SDR|[HX]?26[45]|HEVC|AV1|AAC|REMUX)(?:-[\p{L}\p{N}-]+)?$");
    private static readonly Regex UnparsedRange = Pattern(@"^[\s._]*(?:[\p{Pd}\u2212&,+/]|and|to|ו)\s*(?:(?:Episodes?|E|פרקים|פרק)\s*)?[0-9]+(?![\p{L}\p{N}])");
    private static readonly Regex EpisodeHint = Pattern(@"(?<![\p{L}\p{N}])(?:S\d|\d+x\d|(?:Season|Episodes?|עונה|פרקים|פרק)\s*\d)");

    public static string CreateFileName(VideoInfo video, int messageId)
    {
        // A remote name is data, never a path, including on non-Windows test runners.
        var original = LimitInput(video.FileName).Replace('\\', '/').Split('/')[^1];
        var suppliedExtension = Path.GetExtension(original);
        var extension = VideoExtensions.Contains(suppliedExtension) ? suppliedExtension :
            MimeExtensions.GetValueOrDefault(video.MimeType?.Split(';')[0].Trim() ?? "", ".bin");
        var stem = suppliedExtension.Length > 0 ? original[..^suppliedExtension.Length] : original;
        if (EpisodeHint.IsMatch(suppliedExtension) || ReleaseExtension.IsMatch(suppliedExtension))
            stem = original;
        stem = Clean(stem);
        if (IsUseful(stem))
        {
            var originalEpisode = TryEpisodeName(stem, extension, preserveSuffix: true);
            if (originalEpisode is not null) return originalEpisode;
            // Preserve ambiguous/incomplete original identities instead of combining sources.
            if (EpisodeHint.IsMatch(stem)) return Finish(stem, extension, messageId);
        }

        var lines = LimitInput(video.PostText).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim()).Where(line => line.Length > 0 &&
                !line.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !line.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && !line.StartsWith('@')).Take(20).ToArray();
        foreach (var line in lines)
        {
            var episodeName = TryEpisodeName(line, extension, preserveSuffix: false);
            if (episodeName is not null) return episodeName;
        }
        for (var index = 1; index < lines.Length; index++)
        {
            if (!EpisodeOnly.IsMatch(lines[index]) || EpisodeOnly.IsMatch(lines[index - 1])) continue;
            var episodeName = TryEpisodeName(lines[index - 1] + " " + lines[index], extension, preserveSuffix: false);
            if (episodeName is not null) return episodeName;
        }
        if (IsUseful(stem)) return Finish(stem, extension, messageId);
        var title = lines.Select(Clean).FirstOrDefault(line => IsUseful(line) && !EpisodeOnly.IsMatch(line));
        return Finish(title ?? $"video-{messageId}", extension, messageId);
    }

    private static string? TryEpisodeName(string source, string extension, bool preserveSuffix)
    {
        Match match = SeasonEpisode.Match(source);
        if (!match.Success) match = CrossNotation.Match(source);
        if (!match.Success) match = LabelNotation.Match(source);
        if (!match.Success || match.Index == 0) return null;
        var title = SceneText(source[..match.Index]);
        if (!IsUseful(title) || EpisodeOnly.IsMatch(title)) return null;
        var trailing = source[(match.Index + match.Length)..];
        // Conflicting identities must not be silently collapsed into a single episode.
        if (SeasonEpisode.IsMatch(trailing) || CrossNotation.IsMatch(trailing) || LabelNotation.IsMatch(trailing)) return null;
        if (!int.TryParse(match.Groups["season"].Value, out var season) ||
            !int.TryParse(match.Groups["episode"].Value, out var episode) || episode == 0) return null;
        var identity = new StringBuilder(FormattableString.Invariant($"S{season:D2}E{episode:D2}"));
        var previous = episode;
        foreach (Match extra in MoreEpisodes.Matches(match.Groups["more"].Value.Replace(" ", "").Replace("_", "").Replace(".", "")))
        {
            var number = int.Parse(extra.Groups["episode"].Value, CultureInfo.InvariantCulture);
            if (number <= previous) return null;
            identity.Append(extra.Groups["range"].Success ? "-E" : "E").Append(number.ToString("D2", CultureInfo.InvariantCulture));
            previous = number;
        }
        // Label/range syntax that was not fully parsed remains untouched instead of losing episodes.
        if (UnparsedRange.IsMatch(trailing)) return null;
        var suffix = preserveSuffix ? SceneText(trailing) : "";
        // Preserve a release-group delimiter immediately following the episode marker.
        var separator = trailing.TrimStart(' ', '.', '_').StartsWith('-') ? "-" : ".";
        var identityText = identity.ToString();
        var suffixBudget = Math.Min(80, MaximumNameLength - extension.Length - identityText.Length - 4);
        suffix = Truncate(suffix, suffixBudget).TrimEnd('.', ' ', '-');
        var titleBudget = MaximumNameLength - extension.Length - identityText.Length - 1 -
            (suffix.Length == 0 ? 0 : suffix.Length + 1) - (IsReserved(title) ? 1 : 0);
        title = Truncate(title, titleBudget).TrimEnd('.', ' ', '-');
        var name = title + "." + identityText + (suffix.Length == 0 ? "" : separator + suffix);
        return Finish(name, extension, 0);
    }

    private static string SceneText(string value)
    {
        var result = new StringBuilder();
        foreach (var rune in Clean(value).EnumerateRunes())
        {
            if (Rune.IsLetterOrDigit(rune) || Rune.GetUnicodeCategory(rune) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark)
                result.Append(rune);
            else if (rune.Value == '-') result.Append('-');
            else if (result.Length > 0 && result[^1] != '.') result.Append('.');
        }
        return result.ToString().Trim('.', '-', ' ');
    }

    private static string Clean(string value)
    {
        var result = new StringBuilder();
        foreach (var rune in value.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.Surrogate) continue;
            result.Append(rune.IsAscii && "<>:\"/\\|?*".Contains((char)rune.Value) ? "_" : rune.ToString());
        }
        return result.ToString().Normalize(NormalizationForm.FormC).Trim().TrimEnd(' ', '.');
    }

    private static bool IsUseful(string value) => value.Any(char.IsLetterOrDigit) && !GenericName.IsMatch(value);
    private static string Finish(string stem, string extension, int messageId)
    {
        stem = Clean(stem);
        if (string.IsNullOrWhiteSpace(stem)) stem = $"video-{messageId}";
        if (IsReserved(stem)) stem = "_" + stem;
        stem = Truncate(stem, MaximumNameLength - extension.Length).TrimEnd(' ', '.');
        return stem + extension;
    }
    private static bool IsReserved(string name)
    {
        var device = name.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
        return device is "CON" or "PRN" or "AUX" or "NUL" or "CLOCK$" or "CONIN$" or "CONOUT$" ||
            (device.Length == 4 && (device.StartsWith("COM", StringComparison.Ordinal) || device.StartsWith("LPT", StringComparison.Ordinal)) &&
                (char.IsAsciiDigit(device[3]) || "¹²³".Contains(device[3])));
    }
    private static string Truncate(string value, int length)
    {
        if (value.Length <= length) return value;
        if (length > 0 && char.IsHighSurrogate(value[length - 1])) length--;
        return value[..Math.Max(0, length)];
    }
    private static string LimitInput(string? value) => Truncate(value ?? "", 4096);
}

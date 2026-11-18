using TelegramDownloader.Core;

namespace TelegramDownloader.Tests;

public sealed class VideoFileNamerTests
{
    [Theory]
    [InlineData("ornek_S01E11.mp4", null, null, "ornek.S01E11.mp4")]
    [InlineData("Example Show 1x2 1080p WEB-DL.mkv", null, null, "Example.Show.S01E02.1080p.WEB-DL.mkv")]
    [InlineData("Show s1e2.mp4", null, null, "Show.S01E02.mp4")]
    [InlineData("Show Season 1 Episode 2.mp4", null, null, "Show.S01E02.mp4")]
    [InlineData("video_42.mp4", "דוגמה עונה 1 פרק 11", null, "דוגמה.S01E11.mp4")]
    [InlineData("file_42.mkv", "Example Show Season 1 Episode 2", null, "Example.Show.S01E02.mkv")]
    [InlineData("document123.mp4", "Example Show\nSeason 1 Episode 2", null, "Example.Show.S01E02.mp4")]
    [InlineData("Show.S01E01E02.mkv", null, null, "Show.S01E01E02.mkv")]
    [InlineData("Show.S01E01-E03.mkv", null, null, "Show.S01E01-E03.mkv")]
    [InlineData("Show.S00E01.mp4", null, null, "Show.S00E01.mp4")]
    [InlineData("Show.S101E100.mp4", null, null, "Show.S101E100.mp4")]
    [InlineData("Show.S1E1E4.mp4", null, null, "Show.S01E01E04.mp4")]
    [InlineData("Show.S01E11.mp4", "Other Season2 Episode3", null, "Show.S01E11.mp4")]
    [InlineData("Show.S01E11.Pilot.1080p.WEB-DL.H.264-GROUP.mkv", null, null, "Show.S01E11.Pilot.1080p.WEB-DL.H.264-GROUP.mkv")]
    [InlineData("clip.mp4", "Episode11", null, "clip.mp4")]
    [InlineData("Show.S01.mp4", null, null, "Show.S01.mp4")]
    [InlineData("Show.S01E01-S02E02.mp4", null, null, "Show.S01E01-S02E02.mp4")]
    [InlineData("Show.S01E01E01.mp4", null, null, "Show.S01E01E01.mp4")]
    [InlineData("family holiday.mp4", "Another title", null, "family holiday.mp4")]
    [InlineData("Holiday clip.mp4", "Show Name Season 1 Episode 2", null, "Show.Name.S01E02.mp4")]
    [InlineData("Show.S01.mp4", "Other Season 1 Episode 2", null, "Show.S01.mp4")]
    [InlineData("Show.S01E01E01.mp4", "Other Season 1 Episode 2", null, "Show.S01E01E01.mp4")]
    [InlineData("Show.S01E01", null, "video/mp4", "Show.S01E01.mp4")]
    [InlineData("Show.S01E01-E03", null, "video/mp4", "Show.S01E01-E03.mp4")]
    [InlineData("Show.S01E01-03", null, "video/mp4", "Show.S01E01-E03.mp4")]
    [InlineData("Show.S01E01E02", null, "video/mp4", "Show.S01E01E02.mp4")]
    [InlineData("Show.S1E1-E3", null, "video/mp4", "Show.S01E01-E03.mp4")]
    [InlineData("Show.1x02", null, "video/mp4", "Show.S01E02.mp4")]
    [InlineData("Show.1x02 1080p", null, "video/mp4", "Show.S01E02.1080p.mp4")]
    [InlineData("Show.S01E01.1080p", null, "video/mp4", "Show.S01E01.1080p.mp4")]
    [InlineData("Show.S01E01.WEB-DL", null, "video/mp4", "Show.S01E01.WEB-DL.mp4")]
    [InlineData("Show.S01E01.exe", null, "video/mp4", "Show.S01E01.mp4")]
    [InlineData("video.mp4", "Example Show S01E01–03", null, "Example.Show.S01E01-E03.mp4")]
    [InlineData("video.mp4", "Example Show S1E1 — E3", null, "Example.Show.S01E01-E03.mp4")]
    [InlineData("video.mp4", "Example Show S01E01/E02", null, "Example Show S01E01_E02.mp4")]
    [InlineData("video.mp4", "Example Show Season 1 Episodes 1–3", null, "Example Show Season 1 Episodes 1–3.mp4")]
    [InlineData("video.mp4", "New this week\nExample Show S01E02", null, "Example.Show.S01E02.mp4")]
    [InlineData("video.mp4", "New this week\nExample Show\nS01E02", null, "Example.Show.S01E02.mp4")]
    [InlineData("Show.S01E01-GROUP.mkv", null, null, "Show.S01E01-GROUP.mkv")]
    [InlineData("Show 1x2-3.mp4", null, null, "Show 1x2-3.mp4")]
    [InlineData("Show 1x2x3.mp4", null, null, "Show 1x2x3.mp4")]
    [InlineData("Show.S01E01E٠٢.mp4", null, null, "Show.S01E01E٠٢.mp4")]
    [InlineData("Show.S01E01foo.mp4", null, null, "Show.S01E01foo.mp4")]
    [InlineData("Show.S1E2-1080p.WEB-DL.mp4", null, null, "Show.S01E02-1080p.WEB-DL.mp4")]
    [InlineData("Show.S1E2-2160p.WEB-DL.mp4", null, null, "Show.S01E02-2160p.WEB-DL.mp4")]
    [InlineData("Show Season 1 Episodes 1 & 2.mp4", null, null, "Show Season 1 Episodes 1 & 2.mp4")]
    [InlineData("Show Season 1 Episodes 1, 2.mp4", null, null, "Show Season 1 Episodes 1, 2.mp4")]
    [InlineData("Show Season 1 Episodes 1 + 2.mp4", null, null, "Show Season 1 Episodes 1 + 2.mp4")]
    [InlineData("video.mp4", "Show Season 1 Episodes 1 & 2", null, "Show Season 1 Episodes 1 & 2.mp4")]
    [InlineData("video.mp4", "Show Season 1 Episodes 1, 2", null, "Show Season 1 Episodes 1, 2.mp4")]
    [InlineData("video.mp4", "Show Season 1 Episodes 1 + 2", null, "Show Season 1 Episodes 1 + 2.mp4")]
    [InlineData("Show.S1E1 & E2.mp4", null, null, "Show.S1E1 & E2.mp4")]
    [InlineData("video.mp4", "Show S1E1, E2", null, "Show S1E1, E2.mp4")]
    [InlineData("video_42.mp4", "Family holiday", null, "Family holiday.mp4")]
    [InlineData("", "https://t.me/example\nA useful title", "video/mp4", "A useful title.mp4")]
    [InlineData("", null, "video/webm", "video-42.webm")]
    [InlineData("", null, null, "video-42.bin")]
    [InlineData("recording.exe", null, "video/mp4", "recording.mp4")]
    [InlineData("recording.exe", null, null, "recording.bin")]
    [InlineData("video.mp4", "A title.mkv", "video/x-matroska", "A title.mkv.mp4")]
    [InlineData("clip.WEBM", null, "video/mp4", "clip.WEBM")]
    [InlineData("../escape.mp4", null, null, "escape.mp4")]
    [InlineData("..\\escape.mp4", null, null, "escape.mp4")]
    [InlineData("CON.mp4", null, null, "_CON.mp4")]
    [InlineData("COM¹.mp4", null, null, "_COM¹.mp4")]
    [InlineData("video .mp4", null, null, "video-42.mp4")]
    [InlineData("nice. .mp4", null, null, "nice.mp4")]
    [InlineData("video.mp4", "Episode 11", null, "video-42.mp4")]
    public void ProducesDeterministicConservativeNames(string original, string? caption, string? mime, string expected)
        => Assert.Equal(expected, VideoFileNamer.CreateFileName(new VideoInfo(original, 1, new object(), caption, mime), 42));

    [Fact]
    public void LongUnicodeEpisodeTitleKeepsEpisodeIdentityAndExtension()
    {
        var original = string.Concat(Enumerable.Repeat("סדרה😀 ", 80)) + "S01E11.1080p.WEB-DL.mp4";
        var name = VideoFileNamer.CreateFileName(new VideoInfo(original, 1, new object()), 42);
        Assert.True(name.Length <= 180);
        Assert.Contains(".S01E11", name);
        Assert.EndsWith(".mp4", name);
        Assert.DoesNotContain(name, char.IsSurrogate);
        Assert.DoesNotContain(name, char.IsControl);
    }

    [Fact]
    public void PathAndControlCharactersCannotEscapeFolder()
    {
        var name = VideoFileNamer.CreateFileName(new VideoInfo("", 1, new object(), "a/b\\c:stream\u202E.mp4\u0001", "video/mp4"), 42);
        Assert.Equal(name, Path.GetFileName(name));
        Assert.DoesNotContain(name, character => Path.GetInvalidFileNameChars().Contains(character));
        Assert.DoesNotContain('\u202E', name);
        Assert.False(name.EndsWith(' ') || name.EndsWith('.'));
    }
}

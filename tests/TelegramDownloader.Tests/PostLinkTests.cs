using TelegramDownloader.Core;

namespace TelegramDownloader.Tests;

public class PostLinkTests
{
    [Theory]
    [InlineData("https://t.me/c/9000000001/42", null, 9000000001L, 42)]
    [InlineData("https://t.me/c/9000000001/7/42?single", null, 9000000001L, 42)]
    [InlineData("https://t.me/public_channel/42", "public_channel", null, 42)]
    [InlineData(" https://telegram.me/public_channel/7/42?t=10 ", "public_channel", null, 42)]
    public void ParsesExactMessage(string text, string? username, long? channel, int message)
        => Assert.Equal(new PostLink(username, channel, message), PostLink.Parse(text));

    [Theory]
    [InlineData("")]
    [InlineData("http://t.me/channel/1")]
    [InlineData("https://t.me.evil.example/channel/1")]
    [InlineData("https://evil.example/t.me/channel/1")]
    [InlineData("https://user@t.me/channel/1")]
    [InlineData("https://t.me:444/channel/1")]
    [InlineData("https://t.me/+invitation")]
    [InlineData("https://t.me/joinchat/123")]
    [InlineData("https://t.me/c/0/1")]
    [InlineData("https://t.me/c/-100123/1")]
    [InlineData("https://t.me/c/9223372036854775808/1")]
    [InlineData("https://t.me/c/123/0")]
    [InlineData("https://t.me/c/123/2147483648")]
    [InlineData("https://t.me/channel/1?comment=23")]
    [InlineData("https://t.me/channel/1?%63omment=23")]
    [InlineData("https://t.me/channel/1?unknown=23")]
    [InlineData("https://t.me/channel/1#fragment")]
    [InlineData("https://t.me/channel/1/2/3")]
    [InlineData("https://t.me/c/123/1/")]
    [InlineData("https://t.me/channel//1")]
    [InlineData("https://t.me//channel/1")]
    public void RejectsUnsupportedLinks(string text)
    {
        var error = Assert.Throws<DownloadException>(() => PostLink.Parse(text));
        Assert.Equal(FailureKind.InvalidInput, error.Kind);
    }
}

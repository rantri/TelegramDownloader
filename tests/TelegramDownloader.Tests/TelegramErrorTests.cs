using TelegramDownloader.Core;
using TL;

namespace TelegramDownloader.Tests;

public sealed class TelegramErrorTests
{
    [Theory]
    [InlineData(400, "PHONE_CODE_INVALID", FailureKind.Authentication)]
    [InlineData(401, "AUTH_KEY_UNREGISTERED", FailureKind.Authentication)]
    [InlineData(400, "CHANNEL_PRIVATE", FailureKind.AccessDenied)]
    [InlineData(403, "CHAT_FORWARDS_RESTRICTED", FailureKind.AccessDenied)]
    [InlineData(400, "MSG_ID_INVALID", FailureKind.NotFound)]
    [InlineData(400, "FILE_REFERENCE_EXPIRED", FailureKind.Network)]
    [InlineData(500, "INTERNAL", FailureKind.Network)]
    public void KnownRpcErrorsProduceSafeActionableCategories(int code, string rpc, FailureKind expected)
    {
        var error = TelegramGateway.TranslateException(new RpcException(code, rpc));
        Assert.Equal(expected, error.Kind);
        Assert.DoesNotContain(rpc, error.Message);
        Assert.NotEmpty(error.Message);
    }

    [Fact]
    public void FloodWaitPreservesReportedDelayWithoutAutomaticallyRetrying()
    {
        var error = TelegramGateway.TranslateException(new RpcException(420, "FLOOD_PREMIUM_WAIT_X", 37));
        Assert.Equal(FailureKind.RateLimited, error.Kind);
        Assert.Contains("37 seconds", error.Message);
        Assert.Equal(-1, TelegramGateway.AutomaticFloodRetryThreshold);
        Assert.InRange(TelegramGateway.MaximumReconnects, 0, 3);
    }

    [Fact]
    public void UnexpectedExceptionDoesNotExposeItsPrivateMessage()
    {
        var error = TelegramGateway.TranslateException(new Exception("private message body and credentials"));
        Assert.Equal(FailureKind.Unexpected, error.Kind);
        Assert.DoesNotContain("private", error.Message);
        Assert.DoesNotContain("credentials", error.Message);
    }
}

namespace TelegramDownloader.Core;

public sealed record AppProfile(int ApiId, string ApiHash, string PhoneNumber)
{
    public void Validate()
    {
        if (ApiId <= 0)
            throw new DownloadException(FailureKind.Authentication, "API ID must be a positive number from my.telegram.org/apps.");
        if (ApiHash is null || ApiHash.Length != 32 || !ApiHash.All(Uri.IsHexDigit))
            throw new DownloadException(FailureKind.Authentication, "API hash must contain the 32 hexadecimal characters from my.telegram.org/apps.");
        if (PhoneNumber is null || PhoneNumber.Length is < 8 or > 16 || PhoneNumber[0] != '+' ||
            PhoneNumber[1] == '0' || PhoneNumber.AsSpan(1).ContainsAnyExceptInRange('0', '9'))
            throw new DownloadException(FailureKind.Authentication, "Phone number must start with + and contain 7 to 15 digits, including the country code.");
    }

    // Records normally print every field. Prevent incidental logging from revealing account material.
    public override string ToString() => "Telegram account profile (private)";
}

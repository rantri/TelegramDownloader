# Public release validation

## Local Windows x64 checks

- .NET SDK 11.0.100 completed locked restore, Release builds of the solution and synthetic GUI harness, and all 182 automated tests. Builds reported zero warnings and errors.
- The self-contained executable returned help and reported `TelegramDownloader 2.0.0` without accessing a Telegram account.
- The privacy review replaced live identifiers in examples, test data and historical validation records. The public baseline starts with a new root. Thirteen earlier development commits remain in the local archive.
- GitHub private vulnerability reporting and Dependabot security updates are enabled for the repository.

The package, clean clone and hosted verification results are recorded as the release completes.

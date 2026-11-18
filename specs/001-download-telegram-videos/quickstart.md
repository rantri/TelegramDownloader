# Quickstart and Validation Guide

Prerequisites for development: Windows x64, .NET SDK 11.0.100, network access for package restore.
Use your own app credentials from https://my.telegram.org/apps for live Telegram checks.
Never put credentials in a command argument, repository file, or test fixture.

## Build and offline validation

```powershell
dotnet restore TelegramDownloader.slnx --locked-mode
dotnet build TelegramDownloader.slnx -c Release --no-restore
dotnet test TelegramDownloader.slnx -c Release --no-build
./scripts/publish.ps1
./artifacts/publish/win-x64/TelegramDownloader.exe --help
```

Copy only the published EXE into an empty directory. Run help and open the GUI. Both must
start. The binary bundles the runtime; native files may extract to the .NET temporary cache.

## Live account validation (manual)

1. Run configure and enter API ID/hash plus phone privately; run login and complete verification.
2. Download an accessible public post to an existing directory. Verify video playback and size.
3. Download an accessible private /c link to an exact filename. Repeat without --overwrite:
   it must fail and preserve the original. Repeat with --overwrite to explicitly replace it.
4. Try a group with Desktop saving disabled. Record actual success or server restriction;
   do not describe this case as verified merely because ordinary downloads work.
5. Cancel a large download; ensure no final file is falsely created and any existing file survives.
6. Open the GUI; configure/sign in if needed, browse to output, download and cancel.
7. Run logout; verify a subsequent operation requests setup/login. This removes local state;
   revoke the client session in Telegram Settings > Devices if server-side revocation is desired.

Record build/tests/publish/GUI evidence and live cases not run in validation.md.

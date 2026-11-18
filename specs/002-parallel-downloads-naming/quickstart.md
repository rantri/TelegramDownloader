# Validation Guide

Use the existing account setup; no credential migration is needed.
This feature changes destinations to existing folders.

## Offline build and publishing

```powershell
dotnet restore TelegramDownloader.slnx --locked-mode
dotnet build TelegramDownloader.slnx -c Release --no-restore
dotnet test TelegramDownloader.slnx -c Release --no-build
./scripts/publish.ps1
./artifacts/publish/win-x64/TelegramDownloader.exe --help
```

Check folder-only CLI validation without connecting: a nonexistent folder or exact filename
must fail with exit5. Tests cover naming tables, concurrency/order/cancel/retry/shutdown,
authentication isolation and concurrent file collisions with synthetic data.

## GUI checks

Open the EXE. Confirm folder Browse, Add, queue grid, Cancel/Retry, and explicit Exit.
Controls remain available while controlled jobs run. Hide/restore preserves visible state.
An idle close exits; close while busy hides to tray; explicit Exit cancels/drains all work.

For account-free native checks, build and run the nonshipping harness:

```powershell
dotnet restore tests/TelegramDownloader.GuiSmoke --locked-mode
dotnet build tests/TelegramDownloader.GuiSmoke -c Release --no-restore
./tests/TelegramDownloader.GuiSmoke/bin/Release/net11.0-windows/win-x64/TelegramDownloader.GuiSmoke.exe
```

It seeds five controlled jobs using a temporary profile/folder. Its helper window invokes
the production tray Show/Exit handlers when the OS notification area cannot be automated.
Exit records writer/file cleanup in `artifacts/gui-smoke-result.json`. Physical tray icon
interaction remains a manual check. See the harness README for idle mode and output options.

## Optional manual Telegram checks

Using already-signed-in owner state, select an existing temporary folder and queue distinct
accessible posts. Observe overlapping progress and automatic episode names. Cancel one
while others continue; retry it. Hide/restore from tray. Exit while active and inspect for
completed outputs versus marked/cleaned partial files. Do not log full captions or credentials.

Record evidence and any not-run live cases in validation.md. Do not interpret synthetic
parallel tests as proof of live throughput or all possible naming formats.

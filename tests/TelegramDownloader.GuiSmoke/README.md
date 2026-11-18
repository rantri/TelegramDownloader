# Native GUI smoke check

This manual harness links the production form to a synthetic gateway and an isolated
temporary profile directory. It never opens a Telegram connection or reads the user's
saved account. Five queued synthetic videos produce three active transfers and two waiting
jobs. Each writes 64 KiB, reports progress, and waits until cancelled. The form input is
pre-filled with a sixth synthetic link and the temporary destination folder.

From the repository root:

```powershell
dotnet build tests/TelegramDownloader.GuiSmoke -c Release
./tests/TelegramDownloader.GuiSmoke/bin/Release/net11.0-windows/win-x64/TelegramDownloader.GuiSmoke.exe
```

Use **Add download**, **Cancel selected**, and **Retry selected** while checking that other
rows remain active. Close the busy window and restore it using **Show download window** in
the separate **Synthetic tray commands** helper window. That helper uses reflection to invoke
the actual production tray menu items; **Exit downloads** invokes the production tray Exit
handler. Closing the helper also invokes Exit so fake transfers are cleaned up. It uses no
Windows UI Automation or native window APIs and does not change production behavior.

The physical Windows notification-area icon, tooltip, context menu, and double-click still
need a manual check when automation cannot access the notification area. The primary window
title contains **synthetic UI check** to distinguish it from the real app. Account setup and
sign-in are unnecessary for this check.

For an empty queue, pass `--idle` and close the primary window normally to check idle exit.
An optional positional argument selects the result path:

```powershell
./tests/TelegramDownloader.GuiSmoke/bin/Release/net11.0-windows/win-x64/TelegramDownloader.GuiSmoke.exe --idle artifacts/gui-idle-result.json
```

On exit, `artifacts/gui-smoke-result.json` records peak concurrency, row states, busy hidden
and restored transitions, and whether all fake writers stopped with no partial or falsely
completed files. A failing cleanup check
returns exit code 1. The JSON contains only synthetic links and a temporary directory path;
the empty scratch directory is left available for inspection. The harness is not part of
the shipped application or its solution build.

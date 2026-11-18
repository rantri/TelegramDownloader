# Third-party licenses and notices

TelegramDownloader's own license is in the root `LICENSE` file.

## Windows distribution

| Component | Attribution and source |
| --- | --- |
| WTelegramClient 4.4.10 | Copyright © Olivier Marcoux 2021–2026, as stated in its NuGet metadata. The archive includes the [license from the package's source revision](https://github.com/wiz0u/WTelegramClient/blob/7c2baece8ab84e88899c5b04ef7d8ad19b4ef29b/LICENSE.txt). |
| .NET runtime | The archive includes `NETCore-LICENSE.txt` and `NETCore-THIRD-PARTY-NOTICES.txt` from the runtime pack used to build the executable. These also cover the framework's System.Security.Cryptography.ProtectedData component. |
| Windows Desktop and Windows Forms | The archive includes `WindowsDesktop-LICENSE.txt` from the Windows Desktop runtime pack and the [Windows Forms third-party notices](https://github.com/dotnet/dotnet/blob/297acf930fb0f5c07fd4e51d78e521b4ad2e5d1f/src/winforms/THIRD-PARTY-NOTICES.TXT) from the .NET 11.0.0 source revision. |

When updating dependencies or the .NET SDK, review the source revisions in the new packages' NuGet metadata and update the checked-in texts and source links. The packaging script selects runtime pack license files using the exact versions from the application's restored NuGet assets.

## Source tooling

The repository includes templates, scripts, and skills from GitHub Spec Kit 1.4.0 under `.specify/` and `.agents/skills/`. Its full [MIT license](Spec-Kit-LICENSE.txt) credits GitHub, Inc. [Upstream source](https://github.com/github/spec-kit/tree/v1.4.0).

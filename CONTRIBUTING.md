# Contributing to TelegramDownloader

The app runs on Windows x64. To try a release, follow the [installation guide](README.md#get-the-windows-app). To change the app, fork this repository, clone your fork, and create a branch from `main`.

## Run the checks

Install Git, PowerShell 7 and .NET SDK 11.0.100 or a patch accepted by [global.json](global.json). Restore requires internet access. Open PowerShell 7 in the checkout and run the same verification used by CI.

```powershell
./scripts/verify.ps1
```

This restores locked dependencies, builds in Release mode, runs the automated tests, builds the synthetic GUI harness, publishes the app and smoke-tests the executable. Tests use synthetic data. For an interactive change, run the [GUI harness](tests/TelegramDownloader.GuiSmoke/README.md) on a Windows desktop. Describe any manual checks in your PR.

- `src/TelegramDownloader.Core` owns post resolution, account storage, naming and transfer behavior.
- `src/TelegramDownloader.App` owns the CLI and Windows Forms entry points.
- `tests/TelegramDownloader.Tests` holds the automated regression suite.
- `specs/` records expected behavior and validation. For a feature, write down its user-visible behavior and acceptance criteria before implementing it. See the [project constitution](.specify/memory/constitution.md).

Keep changes focused. Add success and failure coverage when changing authentication, transfers, retries or file handling. For an intentional dependency change, regenerate affected NuGet lock files, review the diff and rerun verification.

## Report an issue or open a pull request

Use [Issues](https://github.com/rantri/TelegramDownloader/issues/new/choose) for bugs and feature requests. Include your Windows version, app version, steps and expected result. Use synthetic links and filenames. Keep private post links, phone numbers, credentials, session files, private media and unredacted logs out of issues and pull requests. Use the [private reporting instructions](SECURITY.md) for security issues.

Push the branch to your fork and open a pull request against `rantri/TelegramDownloader:main`. Explain the user-visible change and how you verified it. GitHub checks each newly pushed commit in this repository and the proposed merge of each PR. Fork PRs run with read-only permissions. GitHub may ask a maintainer to approve the first workflow run from a new contributor.

## Maintainer release procedure

1. Choose a stable three-part version. Increment the major number for an incompatible change, minor for a compatible feature and patch for a compatible fix. Update `<Version>` in [the app project](src/TelegramDownloader.App/TelegramDownloader.App.csproj). Write `docs/releases/<version>.md` for user-facing changes and migration steps where relevant.
2. From the reviewed revision, run `./scripts/verify.ps1` and `./scripts/package.ps1 -Tag v2.0.0`, substituting the chosen version. Inspect the ZIP and manifest under `artifacts/release/`.
3. Merge the change into `main` and confirm CI passed. Tag the exact release commit and push only that tag. Replace the example version for future releases.

   ```powershell
   git switch main
   git pull --ff-only
   git tag -a v2.0.0 -m "TelegramDownloader 2.0.0"
   git push origin refs/tags/v2.0.0
   ```

4. Watch the [release workflow](https://github.com/rantri/TelegramDownloader/actions/workflows/release.yml). It checks the tagged code, compares the tag and executable version, then publishes the verified ZIP, checksum and release notes. Download both files from the release and repeat the README verification.
5. If a job fails, inspect its logs. For a transient failure, rerun the failed workflow. If an interrupted upload left a draft, inspect it in Releases and discard the incomplete draft before retrying the same verified tag. For a defect in an already published version, release a new patch version. Keep published tags and assets stable.

The project is distributed under the [MIT license](LICENSE). The release package includes the upstream notices for its dependencies.

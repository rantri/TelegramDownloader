# Validation quickstart

1. On Windows x64, clone the repository into an empty folder. Install PowerShell 7 and .NET SDK 11.0.100 or an allowed patch.
2. Run `./scripts/verify.ps1`. Confirm tests pass, the manual GUI harness builds and the published executable answers help and version.
3. Run `./scripts/package.ps1 -Tag v0.0.0` against version 2.0.0. Expect failure. Run again with the matching tag. Inspect the archive entries and compare its SHA-256 to `SHA256SUMS.txt`.
4. Push two related commits together on a contributor branch. Inspect each checked SHA, open a PR and confirm its proposed merge also passes.
5. Tag the verified revision. Download the two published assets into a fresh directory, verify the checksum, extract, and invoke the executable's help and version. Follow the issue and private advisory links from the contributor guide.
6. Record results and links in `validation.md`. No step requires a Telegram login.

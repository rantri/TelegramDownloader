# Contributor and release contract

- Branch pushes verify newly introduced revisions. Pull requests verify the proposed merge using read-only permissions.
- Run `./scripts/verify.ps1` on Windows x64 with PowerShell 7 and the SDK selected by `global.json`. A nonzero exit means verification failed.
- Run `./scripts/package.ps1 -Tag v2.0.0` after verification. A tag must match the binary version exactly. The package script reports failure for an invalid candidate.
- Download the versioned ZIP and `SHA256SUMS.txt` from the same release. Compare the ZIP digest to the entry for its filename before extraction.
- Maintainers update the project version, merge the tested change and push the matching stable tag. The release job consumes the verified package. It must preserve any previously published assets.

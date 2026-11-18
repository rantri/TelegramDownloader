Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$failed = @()
$invalidSdkRevisions = $env:INVALID_SDK_REVISIONS | ConvertFrom-Json
foreach ($sha in ($env:REVISIONS | ConvertFrom-Json)) {
    Write-Host "::group::Verify $sha"
    $worktree = Join-Path $env:RUNNER_TEMP "verify-$sha"
    try {
        if ($invalidSdkRevisions -contains $sha) {
            throw 'global.json is missing or does not specify a valid SDK version.'
        }
        & git worktree add --detach $worktree $sha
        if ($LASTEXITCODE -ne 0) { throw "Could not check out $sha." }
        & (Join-Path $worktree 'scripts/verify.ps1')
        "- Passed $sha" >> $env:GITHUB_STEP_SUMMARY
    }
    catch {
        $failed += $sha
        Write-Host "::error::Verification failed for $sha. $_"
        "- Failed $sha" >> $env:GITHUB_STEP_SUMMARY
    }
    finally {
        Write-Host '::endgroup::'
    }
    # Each path was freshly created under this ephemeral hosted runner's temp directory.
    if (Test-Path -LiteralPath $worktree) {
        & git worktree remove --force $worktree
        if ($LASTEXITCODE -ne 0) { throw "Could not release the worktree for $sha." }
    }
}
if ($failed.Count) { throw "Verification failed for $($failed -join ', ')." }

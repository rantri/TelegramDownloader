[CmdletBinding()]
param(
    [Alias('Output')]
    [ValidateNotNullOrEmpty()]
    [string] $OutputDirectory = (Join-Path $PSScriptRoot '../artifacts/publish/win-x64')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($env:OS -ne 'Windows_NT') {
    throw 'Publish this Windows desktop application on Windows with the .NET 11 SDK.'
}

$repositoryDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectFile = Join-Path $repositoryDirectory 'src/TelegramDownloader.App/TelegramDownloader.App.csproj'
$publishDirectory = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
$executableName = 'TelegramDownloader.exe'

if (-not (Test-Path -LiteralPath $projectFile -PathType Leaf)) {
    throw "Application project not found: $projectFile"
}
if (Test-Path -LiteralPath $publishDirectory) {
    if (-not (Test-Path -LiteralPath $publishDirectory -PathType Container)) {
        throw "The publish destination is a file: $publishDirectory"
    }
    $unrelatedItems = @(Get-ChildItem -LiteralPath $publishDirectory -Force |
        Where-Object { $_.PSIsContainer -or $_.Name -ne $executableName })
    if ($unrelatedItems.Count -gt 0) {
        throw 'The output directory contains other files or folders. Choose an empty directory; nothing was deleted.'
    }
}

$null = Get-Command dotnet -ErrorAction Stop
$null = [IO.Directory]::CreateDirectory($publishDirectory)

# Running from the repository makes global.json select the pinned SDK even when
# this script is invoked from another working directory.
Push-Location -LiteralPath $repositoryDirectory
try {
    & dotnet publish $projectFile `
        --configuration Release `
        --runtime win-x64 `
        --self-contained true `
        --output $publishDirectory `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:PublishTrimmed=false `
        -p:PublishAot=false `
        -p:RestoreLockedMode=true `
        -p:DebugType=embedded

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}

$publishedItems = @(Get-ChildItem -LiteralPath $publishDirectory -Force)
if ($publishedItems.Count -ne 1 -or $publishedItems[0].PSIsContainer -or
    $publishedItems[0].Name -ne $executableName -or $publishedItems[0].Length -le 0) {
    throw "Single-file verification failed in $publishDirectory. Expected only a nonempty $executableName; no files were deleted."
}

Write-Host "Published: $($publishedItems[0].FullName)"
Write-Host "Size: $($publishedItems[0].Length) bytes"

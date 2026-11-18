#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $Tag
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($Tag -cnotmatch '\Av(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\z') {
    throw "Expected a stable release tag such as v2.0.0. Received '$Tag'."
}
$version = $Tag.Substring(1)
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$binary = Join-Path $root 'artifacts/publish/win-x64/TelegramDownloader.exe'
if (!(Test-Path -LiteralPath $binary -PathType Leaf)) {
    throw 'Run ./scripts/verify.ps1 first to build and verify the release executable.'
}

Push-Location -LiteralPath $root
try {
    $propertiesJson = & dotnet msbuild 'src/TelegramDownloader.App/TelegramDownloader.App.csproj' -nologo `
        -p:Configuration=Release -getProperty:Version,ProjectAssetsFile
    if ($LASTEXITCODE -ne 0) { throw 'Could not read the application build properties.' }
    $project = ($propertiesJson | Out-String | ConvertFrom-Json).Properties
}
finally { Pop-Location }

if ($version -cne $project.Version) {
    throw "Tag '$Tag' does not match project version '$($project.Version)'."
}
$reportedVersion = & $binary --version
if ($LASTEXITCODE -ne 0 -or ($reportedVersion -join "`n") -cne "TelegramDownloader $version") {
    throw "The published executable does not report version $version. Run ./scripts/verify.ps1."
}

$assets = Get-Content -LiteralPath $project.ProjectAssetsFile -Raw | ConvertFrom-Json -AsHashtable
function Find-RuntimePack([string] $name) {
    $ranges = @($assets.project.frameworks.Values.downloadDependencies |
        Where-Object name -eq $name | Select-Object -ExpandProperty version -Unique)
    if ($ranges.Count -ne 1 -or $ranges[0] -notmatch '^\[([^,\]]+)(?:,\s*\1)?\]$') {
        throw "Could not identify the exact restored version of $name."
    }
    $packVersion = $Matches[1]
    foreach ($folder in $assets.packageFolders.Keys) {
        $directory = Join-Path $folder $name.ToLowerInvariant() $packVersion
        if (Test-Path -LiteralPath $directory -PathType Container) { return $directory }
    }
    throw "Could not find restored runtime pack $name $packVersion. Run ./scripts/verify.ps1."
}

$runtime = Find-RuntimePack 'Microsoft.NETCore.App.Runtime.win-x64'
$desktop = Find-RuntimePack 'Microsoft.WindowsDesktop.App.Runtime.win-x64'
$files = [ordered]@{
    'TelegramDownloader.exe' = $binary
    'LICENSE' = Join-Path $root 'LICENSE'
    'licenses/README.md' = Join-Path $root 'licenses/README.md'
    'licenses/WTelegramClient-LICENSE.txt' = Join-Path $root 'licenses/WTelegramClient-LICENSE.txt'
    'licenses/NETCore-LICENSE.txt' = Join-Path $runtime 'LICENSE.TXT'
    'licenses/NETCore-THIRD-PARTY-NOTICES.txt' = Join-Path $runtime 'THIRD-PARTY-NOTICES.TXT'
    'licenses/WindowsDesktop-LICENSE.txt' = Join-Path $desktop 'LICENSE'
    'licenses/WindowsForms-THIRD-PARTY-NOTICES.txt' = Join-Path $root 'licenses/WindowsForms-THIRD-PARTY-NOTICES.txt'
    'licenses/Spec-Kit-LICENSE.txt' = Join-Path $root 'licenses/Spec-Kit-LICENSE.txt'
}

$release = Join-Path $root 'artifacts/release'
$null = [IO.Directory]::CreateDirectory($release)
$zipName = "TelegramDownloader-$version-win-x64.zip"
$tempPrefix = Join-Path $release ([Guid]::NewGuid().ToString('N'))
$tempZip = "$tempPrefix.zip.tmp"
$tempSums = "$tempPrefix.sha256.tmp"
# Serialize updates to the ZIP and its shared manifest within this checkout.
$lock = [IO.FileStream]::new((Join-Path $root 'artifacts/package.lock'), [IO.FileMode]::CreateNew,
    [IO.FileAccess]::Write, [IO.FileShare]::None, 4096, [IO.FileOptions]::DeleteOnClose)
try {
    $archive = [IO.Compression.ZipFile]::Open($tempZip, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in $files.GetEnumerator()) {
            if ((Get-Item -LiteralPath $file.Value).Length -le 0) { throw "Empty package file: $($file.Key)" }
            $entry = $archive.CreateEntry($file.Key)
            # Use a fixed timestamp so identical inputs produce the same digest on repeat packaging.
            $entry.LastWriteTime = [DateTimeOffset]::new(2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
            $inputFile = [IO.File]::OpenRead($file.Value)
            try {
                $output = $entry.Open()
                try { $inputFile.CopyTo($output) }
                finally { $output.Dispose() }
            }
            finally { $inputFile.Dispose() }
        }
    }
    finally { $archive.Dispose() }

    $hash = (Get-FileHash -LiteralPath $tempZip -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText($tempSums, "$hash  $zipName`n", [Text.UTF8Encoding]::new($false))
    [IO.File]::Move($tempZip, (Join-Path $release $zipName), $true)
    [IO.File]::Move($tempSums, (Join-Path $release 'SHA256SUMS.txt'), $true)
    Write-Host "Packaged $zipName ($hash)"
}
finally {
    $lock.Dispose()
    [IO.File]::Delete($tempZip)
    [IO.File]::Delete($tempSums)
}

#requires -Version 7.0
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $IsWindows -or [Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne 'X64') {
    throw 'Run verification on Windows x64 with the SDK selected by global.json.'
}

function Invoke-DotNet {
    & dotnet @args
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($args -join ' ') failed with exit code $LASTEXITCODE."
    }
}

$solution = 'TelegramDownloader.slnx'
$harness = 'tests/TelegramDownloader.GuiSmoke/TelegramDownloader.GuiSmoke.csproj'
$app = 'src/TelegramDownloader.App/TelegramDownloader.App.csproj'
Push-Location -LiteralPath (Join-Path $PSScriptRoot '..')
try {
    Invoke-DotNet restore $solution --locked-mode
    Invoke-DotNet build $solution -c Release --no-restore
    Invoke-DotNet test $solution -c Release --no-build --no-restore
    Invoke-DotNet restore $harness --locked-mode
    Invoke-DotNet build $harness -c Release --no-restore

    & (Join-Path $PSScriptRoot 'publish.ps1')
    $executable = './artifacts/publish/win-x64/TelegramDownloader.exe'
    $version = (Invoke-DotNet msbuild $app -nologo '-getProperty:Version').Trim()
    $reported = & $executable --version
    if ($LASTEXITCODE -ne 0 -or "$reported" -cne "TelegramDownloader $version") {
        throw "Published executable reported '$reported'. Expected TelegramDownloader $version."
    }
    $helpText = & $executable --help | Out-String
    if ($LASTEXITCODE -ne 0 -or $helpText -notmatch 'TelegramDownloader\.exe download') {
        throw 'Published executable did not return its download usage.'
    }
    Write-Host "Verified TelegramDownloader $version for Windows x64."
}
finally {
    Pop-Location
}

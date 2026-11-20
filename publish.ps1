# Publishes self-contained, single-file builds for every platform.
# Usage:  ./publish.ps1                     (all platforms)
#         ./publish.ps1 win-x64 osx-arm64   (only the listed ones)

param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Runtimes)

if (-not $Runtimes) { $Runtimes = @('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64') }

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'ProductDescGen.csproj'

foreach ($rid in $Runtimes) {
    Write-Host "==> Publishing $rid" -ForegroundColor Cyan
    dotnet publish $project -p:PublishProfile=$rid
    if ($LASTEXITCODE -ne 0) { throw "Publish failed for $rid" }
}

Write-Host "Done. Output is in bin/publish/<platform>/" -ForegroundColor Green

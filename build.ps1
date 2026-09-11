[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [Alias("c")]
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$solution = Join-Path $PSScriptRoot "Shackles.slnx"

Write-Host "Restoring Shackles..."
& dotnet restore $solution -m:1 --disable-build-servers
if ($LASTEXITCODE -ne 0) {
    throw "dotnet restore failed with exit code $LASTEXITCODE."
}

Write-Host "Building Shackles ($Configuration)..."
& dotnet build $solution --configuration $Configuration --no-restore -m:1 --disable-build-servers
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE."
}

Write-Host "Shackles $Configuration build completed successfully."

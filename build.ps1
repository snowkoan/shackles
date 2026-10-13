[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [Alias("c")]
    [ValidateSet("Debug", "Release", "All")]
    [string]$Configuration = "Debug",

    [switch]$Publish,

    [Alias("Tests")]
    [switch]$Test,

    [switch]$InteractiveTests
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$solution = Join-Path $PSScriptRoot "Shackles.slnx"
$appProject = Join-Path $PSScriptRoot "src\Shackles.App\Shackles.App.csproj"
$wespClientPath = Join-Path $PSScriptRoot "local-dependencies\wesp\espclient.dll"

if (Test-Path -LiteralPath $wespClientPath -PathType Leaf) {
    Write-Host "Optional WESP client found; it will be included in build and publish outputs."
}
else {
    Write-Host "Optional WESP client not found; building without espclient.dll."
    Write-Host "To include it, place an authorized x64 client in local-dependencies\wesp\espclient.dll."
}

if ($Publish -and -not $PSBoundParameters.ContainsKey("Configuration")) {
    $Configuration = "Release"
}

Write-Host "Restoring Shackles..."
& dotnet restore $solution -m:1 --disable-build-servers
if ($LASTEXITCODE -ne 0) {
    throw "dotnet restore failed with exit code $LASTEXITCODE."
}

$configurations = if ($Configuration -eq "All") { @("Debug", "Release") } else { @($Configuration) }
foreach ($buildConfiguration in $configurations) {
    Write-Host "Building Shackles ($buildConfiguration)..."
    & dotnet build $solution --configuration $buildConfiguration --no-restore -m:1 --disable-build-servers
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build ($buildConfiguration) failed with exit code $LASTEXITCODE."
    }

    Write-Host "Shackles $buildConfiguration build completed successfully."

    if ($Publish) {
        $publishDirectory = Join-Path $PSScriptRoot "artifacts\publish\win-x64\$buildConfiguration"
        Write-Host "Publishing Shackles ($buildConfiguration, win-x64, framework-dependent single file)..."
        & dotnet publish $appProject --configuration $buildConfiguration -r win-x64 `
            --self-contained false -p:PublishSingleFile=true -p:DebugType=embedded `
            -p:PublishDocumentationFile=false -p:PublishReferencesDocumentationFiles=false `
            --output $publishDirectory -m:1 --disable-build-servers
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet publish ($buildConfiguration) failed with exit code $LASTEXITCODE."
        }

        Write-Host "Shackles $buildConfiguration package: $publishDirectory"
        if (Test-Path -LiteralPath (Join-Path $publishDirectory "espclient.dll") -PathType Leaf) {
            Write-Host "This local package includes the optional WESP client; distribution requires appropriate permission."
        }
        else {
            Write-Host "This package omits the optional WESP client; the WESP page will explain setup if no client is installed."
        }
        Write-Host "The .NET 10 Desktop Runtime is required."
    }
}

if ($Test -or $InteractiveTests) {
    $testFailures = @()
    foreach ($testConfiguration in $configurations) {
        Write-Host "Testing Shackles ($testConfiguration)..."
        $testArguments = @("test", $solution, "--configuration", $testConfiguration,
            "--no-build", "--no-restore", "-m:1", "--disable-build-servers")
        if (-not $InteractiveTests) {
            $testArguments += @("--filter", "TestCategory!=Interactive")
        }
        & dotnet @testArguments
        if ($LASTEXITCODE -ne 0) {
            $testFailures += "$testConfiguration (exit code $LASTEXITCODE)"
        }
        else {
            Write-Host "Shackles $testConfiguration tests completed successfully."
        }
    }

    if ($testFailures.Count -gt 0) {
        # Preserve failure status even when a later configuration passed.
        $global:LASTEXITCODE = 1
        throw "dotnet test failed for: $($testFailures -join ', ')."
    }
}

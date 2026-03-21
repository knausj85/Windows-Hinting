# Build-HintOverlay.ps1
# Build HintOverlay using .NET Framework MSBuild

<#
.SYNOPSIS
    Builds HintOverlay using Visual Studio's .NET Framework MSBuild
.DESCRIPTION
    Uses the full MSBuild.exe from Visual Studio (not dotnet CLI)
    This supports COM references like UIAutomationClient
.PARAMETER Configuration
    Build configuration: Debug or Release (default: Release)
.PARAMETER Clean
    Clean before building (default: false)
.EXAMPLE
    .\Build-HintOverlay.ps1

    Builds Release configuration
.EXAMPLE
    .\Build-HintOverlay.ps1 -Configuration Debug -Clean

    Clean build Debug configuration
#>

param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [switch]$Clean = $false
)

# Find MSBuild.exe in Visual Studio installation
$possiblePaths = @(
    "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe",
    "C:\Program Files\Microsoft Visual Studio\18\Insiders\MSBuild\Current\Bin\MSBuild.exe", # VS 2026 Insiders
    "C:\Program Files\Microsoft Visual Studio\18\Professional\MSBuild\Current\Bin\MSBuild.exe",
    "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
    "C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe",
    "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
    "C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe"
)

# Find MSBuild
$foundMsbuild = $null
foreach ($path in $possiblePaths) {
    if (Test-Path $path) {
        $foundMsbuild = $path
        break
    }
}

if (-not $foundMsbuild) {
    Write-Host "✗ ERROR: MSBuild.exe not found" -ForegroundColor Red
    Write-Host ""
    Write-Host "Please ensure Visual Studio 2022 is installed with:"
    Write-Host "  - Desktop development with C++"
    Write-Host "  - .NET development"
    Write-Host ""
    exit 1
}

Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "HintOverlay Build Tool (.NET Framework MSBuild)" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""

Write-Host "Configuration:" -ForegroundColor Green
Write-Host "  Configuration: $Configuration"
Write-Host "  Clean: $Clean"
Write-Host "  MSBuild: $foundMsbuild"
Write-Host ""

# Clean if requested
if ($Clean) {
    Write-Host "Cleaning..." -ForegroundColor Yellow
    & $foundMsbuild HintOverlay.sln /t:Clean /p:Configuration=$Configuration

    if ($LASTEXITCODE -ne 0) {
        Write-Host "✗ Clean failed" -ForegroundColor Red
        exit 1
    }

    Write-Host "✓ Clean successful" -ForegroundColor Green
    Write-Host ""
}

# Build
Write-Host "Building $Configuration configuration..." -ForegroundColor Yellow

$buildArgs = @(
    "HintOverlay.sln",
    "/p:Configuration=$Configuration",
    "/verbosity:minimal",
    "/nologo"
)

& $foundMsbuild @buildArgs

if ($LASTEXITCODE -eq 0) {
    Write-Host ""
    Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Green
    Write-Host "✓ Build Successful!" -ForegroundColor Green
    Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Green
    Write-Host ""
    Write-Host "Output: bin\$Configuration\net8.0-windows\HintOverlay.exe" -ForegroundColor Green
    Write-Host ""
    Write-Host "Next step: Sign the executable"
    Write-Host "  .\Sign-HintOverlay.ps1 -BuildConfiguration $Configuration" -ForegroundColor Cyan
    Write-Host ""
} else {
    Write-Host ""
    Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Red
    Write-Host "✗ Build Failed!" -ForegroundColor Red
    Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Red
    Write-Host ""
    exit 1
}

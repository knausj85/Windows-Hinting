# Build-InstallerMSI.ps1
# Builds HintOverlay and creates a signed MSI installer

<#
.SYNOPSIS
    Builds HintOverlay and creates an MSI installer
.DESCRIPTION
    1. Builds the application in Release mode
    2. Signs the executable
    3. Builds the WiX installer
    4. Outputs MSI to bin\Release\HintOverlay.msi
.PARAMETER SkipSign
    Skip the code signing step (default: false)
.PARAMETER SkipBuild
    Skip the build step (default: false)
.EXAMPLE
    .\Build-InstallerMSI.ps1

    Full build, sign, and create installer
.EXAMPLE
    .\Build-InstallerMSI.ps1 -SkipSign

    Build and create installer, but don't sign
#>

param(
    [switch]$SkipSign = $false,
    [switch]$SkipBuild = $false
)

# Configuration
$projectRoot = Get-Location
$msbuild = "C:\Program Files\Microsoft Visual Studio\18\Insiders\MSBuild\Current\Bin\MSBuild.exe"
$signtool = "C:\Program Files (x86)\Microsoft Visual Studio\Shared\NuGetPackages\microsoft.windows.sdk.buildtools\10.0.26100.1742\bin\10.0.26100.0\x64\signtool.exe"
$certPath = "$env:USERPROFILE\HintOverlay_CodeSign.pfx"
$certPassword = "test123"  # ⚠️ Store securely in production!
$exePath = "bin\Release\net8.0-windows\HintOverlay.exe"
$msiPath = "HintOverlay.Installer\bin\Release\HintOverlay.msi"

# Colors
$successColor = "Green"
$errorColor = "Red"
$infoColor = "Cyan"

Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor $infoColor
Write-Host "HintOverlay MSI Builder" -ForegroundColor $infoColor
Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor $infoColor
Write-Host ""

# Verify tools exist
if (-not (Test-Path $msbuild)) {
    Write-Host "✗ ERROR: MSBuild not found at $msbuild" -ForegroundColor $errorColor
    exit 1
}

if (-not $SkipSign -and -not (Test-Path $signtool)) {
    Write-Host "✗ ERROR: signtool not found at $signtool" -ForegroundColor $errorColor
    exit 1
}

if (-not $SkipSign -and -not (Test-Path $certPath)) {
    Write-Host "✗ ERROR: Certificate not found at $certPath" -ForegroundColor $errorColor
    exit 1
}

# ============================================================================
# Step 1: Build Application
# ============================================================================

if (-not $SkipBuild) {
    Write-Host "Step 1: Building application..." -ForegroundColor $infoColor
    Write-Host "  Configuration: Release" -ForegroundColor $infoColor
    Write-Host "  Note: Release build automatically patches manifest to uiAccess=true" -ForegroundColor $infoColor

    & $msbuild HintOverlay.sln /p:Configuration=Release /verbosity:minimal /nologo

    if ($LASTEXITCODE -ne 0) {
        Write-Host "✗ Build failed" -ForegroundColor $errorColor
        exit 1
    }

    if (-not (Test-Path $exePath)) {
        Write-Host "✗ ERROR: Executable not found at $exePath" -ForegroundColor $errorColor
        exit 1
    }

    Write-Host "✓ Build successful" -ForegroundColor $successColor
    Write-Host ""
} else {
    Write-Host "Step 1: Skipping build (using existing executable)" -ForegroundColor $infoColor
    Write-Host ""
}

# ============================================================================
# Step 2: Sign Executable
# ============================================================================

if (-not $SkipSign) {
    Write-Host "Step 2: Signing executable..." -ForegroundColor $infoColor

    & $signtool sign /f $certPath /p $certPassword /fd SHA256 /v $exePath

    if ($LASTEXITCODE -ne 0) {
        Write-Host "✗ Signing failed" -ForegroundColor $errorColor
        exit 1
    }

    Write-Host "✓ Signing successful" -ForegroundColor $successColor
    Write-Host ""
} else {
    Write-Host "Step 2: Skipping signing" -ForegroundColor $infoColor
    Write-Host ""
}

# ============================================================================
# Step 3: Build MSI Installer
# ============================================================================

Write-Host "Step 3: Building MSI installer..." -ForegroundColor $infoColor

& $msbuild HintOverlay.Installer\HintOverlay.Installer.wixproj /p:Configuration=Release /verbosity:minimal /nologo

if ($LASTEXITCODE -ne 0) {
    Write-Host "✗ MSI build failed" -ForegroundColor $errorColor
    exit 1
}

if (-not (Test-Path $msiPath)) {
    Write-Host "✗ ERROR: MSI file not found at $msiPath" -ForegroundColor $errorColor
    exit 1
}

# Get MSI info
$msiFile = Get-Item $msiPath
$msiSizeMB = [Math]::Round($msiFile.Length / 1MB, 2)

Write-Host "✓ MSI build successful" -ForegroundColor $successColor
Write-Host "  Path: $msiPath" -ForegroundColor $successColor
Write-Host "  Size: $msiSizeMB MB" -ForegroundColor $successColor
Write-Host ""

# ============================================================================
# Summary
# ============================================================================

Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor $successColor
Write-Host "✓ Build Complete!" -ForegroundColor $successColor
Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor $successColor
Write-Host ""

Write-Host "Installer: $msiPath" -ForegroundColor $successColor
Write-Host ""
Write-Host "Installation:" -ForegroundColor $infoColor
Write-Host "  msiexec /i HintOverlay.msi" -ForegroundColor $infoColor
Write-Host ""
Write-Host "Uninstall:" -ForegroundColor $infoColor
Write-Host "  msiexec /x HintOverlay.msi" -ForegroundColor $infoColor
Write-Host ""

Write-Host "Next steps:" -ForegroundColor $infoColor
Write-Host "  1. Test the MSI: Start > Run > msiexec /i $msiPath" -ForegroundColor $infoColor
Write-Host "  2. Verify installation in Program Files\HintOverlay" -ForegroundColor $infoColor
Write-Host "  3. Test uiAccess functionality" -ForegroundColor $infoColor
Write-Host "  4. Sign MSI (optional, for commercial distributions)" -ForegroundColor $infoColor
Write-Host ""

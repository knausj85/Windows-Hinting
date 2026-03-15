param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$Installer,
    [switch]$SkipSigning,
    [string]$CertPath = "",
    [string]$CertPassword = "HintOverlay_BuildCert_2024"
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Split-Path -Parent $ScriptDir

if ($CertPath -eq "") {
    $CertPath = "$RepoRoot\certs\HintOverlay_CodeSign.pfx"
}

Write-Host "=========================================="
Write-Host "HintOverlay Complete Build Script"
Write-Host "=========================================="
Write-Host "Configuration: $Configuration"
Write-Host "Build Installer: $Installer"
Write-Host "Repository Root: $RepoRoot"
Write-Host ""

$StepCount = if ($Installer) { '3' } else { '1' }

# Step 1: Build the executable (with signing if Release)
Write-Host "[1/$StepCount] Building HintOverlay executable..."
Write-Host ""

$BuildArgs = @(
    "$RepoRoot\HintOverlay.csproj"
    "/p:Configuration=$Configuration"
    "/nologo"
    "/v:minimal"
)

if ((-not $SkipSigning) -and ($Configuration -eq "Release")) {
    $BuildArgs += "/p:CodeSigningCertPath=`"$CertPath`""
    $BuildArgs += "/p:CodeSigningPassword=`"$CertPassword`""
}

msbuild @BuildArgs

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "ERROR: HintOverlay build failed with exit code $LASTEXITCODE"
    exit 1
}

Write-Host ""
Write-Host "[OK] Executable build completed successfully"
Write-Host ""

# Verify signature if Release build
if (($Configuration -eq "Release") -and (-not $SkipSigning)) {
    Write-Host "Verifying executable signature..."
    $ExePath = "$RepoRoot\bin\$Configuration\net8.0-windows\HintOverlay.exe"

    if (Test-Path $ExePath) {
        $sig = Get-AuthenticodeSignature -FilePath $ExePath
        if ($sig.SignerCertificate) {
            Write-Host "[OK] Executable is signed"
            Write-Host "  Subject: $($sig.SignerCertificate.Subject)"
            Write-Host "  Valid until: $($sig.SignerCertificate.NotAfter)"
        }
        else {
            Write-Host "WARNING: Executable does not appear to be signed"
        }
    }
    Write-Host ""
}

# Build installer if requested
if ($Installer) {
    Write-Host "[2/$StepCount] Building MSI installer..."
    Write-Host ""

    $InstallerProject = "$RepoRoot\HintOverlay.Installer2\HintOverlay.Installer2.wixproj"

    msbuild $InstallerProject /p:Configuration=$Configuration /nologo /v:minimal

    if ($LASTEXITCODE -ne 0) {
        Write-Host ""
        Write-Host "ERROR: Installer build failed with exit code $LASTEXITCODE"
        exit 1
    }

    Write-Host ""
    Write-Host "[OK] Installer build completed successfully"
    Write-Host ""

    # Verify MSI contents
    Write-Host "[3/$StepCount] Verifying installer contents..."

    $MsiPath = "$RepoRoot\HintOverlay.Installer2\bin\$Configuration\en-US\HintOverlay.msi"

    if (Test-Path $MsiPath) {
        $MsiSize = (Get-Item $MsiPath).Length / 1MB
        Write-Host "[OK] MSI created successfully"
        Write-Host "  Path: $MsiPath"
        Write-Host "  Size: $($MsiSize.ToString('0.0')) MB"
    }
    else {
        Write-Host ""
        Write-Host "ERROR: MSI file not found at expected location: $MsiPath"
        exit 1
    }

    Write-Host ""
}

Write-Host "=========================================="
Write-Host "[OK] Build completed successfully!"
Write-Host "=========================================="
Write-Host ""
Write-Host "Build Summary:"
Write-Host "  Configuration: $Configuration"
Write-Host "  Executable: bin\$Configuration\net8.0-windows\HintOverlay.exe"
if (($Configuration -eq "Release") -and (-not $SkipSigning)) {
    Write-Host "  Signing: Enabled (with self-signed certificate)"
}
if ($Installer) {
    Write-Host "  Installer: HintOverlay.Installer2\bin\$Configuration\en-US\HintOverlay.msi"
}
Write-Host ""

if ($Installer) {
    Write-Host "Next Steps:"
    Write-Host "  1. Test the installer by running the MSI file"
    Write-Host "  2. Verify the signed executable is included in the installation"
    Write-Host "  3. Test UIAccess functionality in the installed application"
    Write-Host ""
}
